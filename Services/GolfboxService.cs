using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using GolfkollektivetBackend.Models;
using HtmlAgilityPack;
using System.Text.Json;

namespace GolfkollektivetBackend.Services;

public class GolfboxService
{
    private readonly HttpClient _httpClient;

    public GolfboxService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(string? Hcp, string? SelectedGuid)> LoginAndGetHcpAndSelectedGuidAsync(string username, string password)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["loginform.submitted"] = "true",
            ["command"] = "login",
            ["loginform.rurl"] = "",
            ["loginform.username"] = username,
            ["loginform.password"] = password
        });

        var response = await _httpClient.PostAsync("https://www.golfbox.no/login.asp?rUrl=", content);
        if (!response.IsSuccessStatusCode)
            return (null, null);

        var frontPage = await _httpClient.GetStringAsync("https://www.golfbox.no/site/my_golfbox/myFrontPage.asp");
        if (!frontPage.Contains("GolfBox Player"))
            return (null, null);

        var hcpMatch = Regex.Match(frontPage, @"HCP[^0-9]*([0-9]{1,2},[0-9])");
        var hcp = hcpMatch.Success ? hcpMatch.Groups[1].Value : null;

        var selectedGuidMatch = Regex.Match(frontPage, @"newWHSScore\.asp\?selected=\{([A-F0-9\-]+)\}", RegexOptions.IgnoreCase);
        var selectedGuid = selectedGuidMatch.Success ? $"{{{selectedGuidMatch.Groups[1].Value}}}" : null;

        Console.WriteLine(hcp != null
            ? $"📊 Detected HCP: {hcp}"
            : "⚠️ Could not extract HCP from front page.");

        Console.WriteLine(selectedGuid != null
            ? $"🧭 Detected Selected GUID: {selectedGuid}"
            : "⚠️ Could not extract Selected GUID from front page.");

        return (hcp, selectedGuid);
    }

    public async Task<(string PlayerGuid, string MagicName, string MagicValue, List<(string Name, string Guid)> Clubs)> GetDynamicTokenAsync(string selectedGuid)
    {
        var url = $"https://www.golfbox.no/site/my_golfbox/score/whs/newWHSScore.asp?selected={selectedGuid}";
        Console.WriteLine($"🌐 Fetching score form: {url}");

        var html = await _httpClient.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var inputs = doc.DocumentNode.SelectNodes("//input[@type='hidden']");
        if (inputs == null)
            throw new Exception("❌ No hidden input fields found!");

        string playerGuid = null;
        string magicName = null;
        string magicValue = null;

        foreach (var node in inputs)
        {
            var name = node.GetAttributeValue("name", "");
            var value = node.GetAttributeValue("value", "");
            Console.WriteLine($"🔍 Hidden field: {name} = {value}");

            if (Regex.IsMatch(name, @"^[A-F0-9\-]{36}$", RegexOptions.IgnoreCase))
            {
                magicName = name;
                magicValue = value;
            }

            if (name == "fld_PlayerMemberGUID")
            {
                playerGuid = value;
            }
        }

        if (string.IsNullOrEmpty(playerGuid) || string.IsNullOrEmpty(magicName) || string.IsNullOrEmpty(magicValue))
            throw new Exception("❌ Required fields not found in the form");

        Console.WriteLine($"✅ Dynamic field: {magicName} = {magicValue}");
        Console.WriteLine($"✅ Player GUID: {playerGuid}");

        var clubs = new List<(string Name, string Guid)>();
        var clubOptions = doc.DocumentNode.SelectNodes("//select[@id='fld_Club']/option");
        if (clubOptions != null)
        {
            foreach (var option in clubOptions)
            {
                var name = option.InnerText.Trim();
                var guid = option.GetAttributeValue("value", "").Trim();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(guid))
                {
                    clubs.Add((name, guid));
                }
            }
        }

        return (playerGuid, magicName, magicValue, clubs);
    }

    public async Task<(string Par, string Rating, string Slope, string Pcc, string IsHcpQualifying)> FetchCourseStatsAsync(
        string courseGuid,
        string teeGuid,
        string playerGuid,
        string date)
    {
        var formattedDate = DateTime.ParseExact(date, "dd.MM.yyyy", null).ToString("yyyyMMddTHHmmss");

        var url = $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=UpdateStats&ScoreDate={formattedDate}&Course_GUID={courseGuid}&Member_GUID={playerGuid}&Tee_GUID={teeGuid}";
        Console.WriteLine($"📥 Fetching course stats: {url}");

        var json = await _httpClient.GetStringAsync(url);
        Console.WriteLine($"📦 Raw JSON response:\n{json}");

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("Data");

        var par = data.GetProperty("CoursePar").ToString();
        var crRaw = data.GetProperty("CR").GetInt32();
        var crFormatted = (crRaw / 10000.0).ToString("0.0").Replace('.', ',');
        var slope = data.GetProperty("Slope").ToString();

        var pcc = "0";
        var isHcpQualifyingRaw = data.TryGetProperty("IsHCPQualifying", out var hcpProp) && hcpProp.GetBoolean();
        var isHcpQualifying = isHcpQualifyingRaw ? "1" : "0";

        Console.WriteLine($"📐 Course stats: Par={par}, CR={crFormatted}, Slope={slope}, PCC={pcc}, IsHCPQualifying={isHcpQualifying}");

        return (par, crFormatted, slope, pcc, isHcpQualifying);
    }

    public async Task<string> ResolveCourseGuidAsync(
        string clubGuid,
        string courseName,
        string scoreDate,
        string scoreTime)
    {
        var dateTime = DateTime.ParseExact($"{scoreDate} {scoreTime}", "dd.MM.yyyy HH:mm", null);
        var scoreDateIso = dateTime.ToString("yyyyMMddTHHmmss");

        var url = $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=GetCourses&ScoreDate={scoreDateIso}&Club_GUID={clubGuid}";
        Console.WriteLine($"📍 Fetching courses from: {url}");

        var response = await _httpClient.GetStringAsync(url);
        Console.WriteLine($"📦 Raw course response:\n{response}");

        using var jsonDoc = JsonDocument.Parse(response);
        var root = jsonDoc.RootElement;
        if (!root.TryGetProperty("Data", out var data))
            throw new Exception("❌ Could not find 'Data' property in course response.");

        foreach (var course in data.EnumerateArray())
        {
            var name = course.GetProperty("Course_Name").GetString()?.Trim();
            var guid = course.GetProperty("Course_GUID").GetString();

            Console.WriteLine($"📘 Course option: {name} => {guid}");

            if (string.Equals(name, courseName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"✅ Matched course '{courseName}' with GUID {guid}");
                return guid!;
            }
        }

        var availableCourses = string.Join(", ", data.EnumerateArray()
            .Select(c => c.GetProperty("Course_Name").GetString()?.Trim()));
        throw new Exception($"❌ Course '{courseName}' not found. Available: {availableCourses}");
    }

    public async Task<string> ResolveTeeGuidAsync(string courseGuid, string teeName, string teeGender)
    {
        var scoreDate = DateTime.Now.ToString("yyyyMMddTHHmmss");
        var url = $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=GetTees&ScoreDate={scoreDate}&Course_GUID={courseGuid}";
        Console.WriteLine($"📍 Fetching tees from: {url}");

        var json = await _httpClient.GetStringAsync(url);
        Console.WriteLine($"📦 Raw tee response:\n{json}");

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Data", out var teeList))
            throw new Exception("❌ JSON does not contain 'Data' field for tees.");

        string? teeGuid = null;
        var availableTees = new List<string>();

        foreach (var tee in teeList.EnumerateArray())
        {
            var name = tee.GetProperty("Text").GetString()?.Trim() ?? "";
            var gender = tee.GetProperty("Gender").GetString()?.Trim() ?? "";
            var guid = tee.GetProperty("Value").GetString();

            availableTees.Add($"{name} ({gender})");
            Console.WriteLine($"🏷️ Tee option: {name} ({gender}) => {guid}");

            if (string.Equals(name, teeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(gender, teeGender, StringComparison.OrdinalIgnoreCase))
            {
                teeGuid = guid;
                Console.WriteLine($"✅ Matched tee '{teeName}' ({teeGender}) with GUID {teeGuid}");
                break;
            }
        }

        if (string.IsNullOrEmpty(teeGuid))
        {
            var fallback = string.Join(", ", availableTees);
            throw new Exception($"❌ Tee '{teeName}' ({teeGender}) not found for course {courseGuid}. Available: {fallback}");
        }

        return teeGuid;
    }
    public async Task<bool> SubmitScoreAsync(SubmitScoreRequest req, string magicName, string magicValue)
    {
        var (par, rating, slope, pcc, isHcpQualifying) = await FetchCourseStatsAsync(
            req.CourseGuid!, req.TeeGuid!, req.PlayerGuid!, req.ScoreDate);

        var formData = new Dictionary<string, string>
        {
            ["selected"] = req.SelectedGuid,
            ["command"] = "save",
            [magicName] = magicValue,
            ["rUrl"] = "/site/my_golfbox/score/whs/newWHSScore.asp",
            ["isHcpQualifying"] = isHcpQualifying,
            ["fld_PlayerMemberGUID"] = req.PlayerGuid,
            ["chk_IsCounting"] = "on",
            ["fld_MemberGUID"] = req.PlayerGuid,
            ["fld_ScoreDate"] = req.ScoreDate,
            ["fld_ScoreTime"] = req.ScoreTime,
            ["rdo_RoundType"] = "2",
            ["fld_HolesPlayed"] = req.HoleScores.Count.ToString(),
            ["fld_Club"] = req.ClubId,
            ["fld_PCC"] = pcc,
            ["fld_Course"] = req.CourseGuid,
            ["fld_Tee"] = req.TeeGuid,
            ["fld_CoursePar"] = par,
            ["fld_CourseRating"] = rating,
            ["fld_Slope"] = slope,
            ["fld_MarkerMemberGUID"] = req.MarkerGuid,
            ["chk_InputHoleScores"] = "on"
        };

        for (int i = 0; i < req.HoleScores.Count; i++)
            formData[$"ScoreHole_{i}"] = req.HoleScores[i].ToString();

        var content = new FormUrlEncodedContent(formData);

        var response = await _httpClient.PostAsync(
            $"https://www.golfbox.no/site/my_golfbox/score/whs/newWHSScore.asp?selected={req.SelectedGuid}",
            content);

        var body = await response.Content.ReadAsStringAsync();

        return body.Contains("Score er lagret") ||
               response.Headers.Location?.ToString().Contains("listScoresToConfirm.asp") == true;
    }

    public async Task LogoutAsync()
    {
        await _httpClient.GetAsync("https://www.golfbox.no/logoff.asp?sessiontimeout=1");
    }

    public async Task<List<MarkerSearchResult>> SearchMarkerAsync(string name)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = name,
            ["country"] = "NO"
        });

        var response = await _httpClient.PostAsync("https://www.golfbox.no/site/my_golfbox/score/whs/_searchMember.asp", body);
        var html = await response.Content.ReadAsStringAsync();

        var results = new List<MarkerSearchResult>();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var options = doc.DocumentNode.SelectNodes("//select[@id='slc_MarkerSearch4result']/option");
        if (options == null) return results;

        foreach (var opt in options)
        {
            var valueRaw = opt.GetAttributeValue("value", "");
            var match = Regex.Match(valueRaw, @"'g':'(\{[A-F0-9\-]+\})'", RegexOptions.IgnoreCase);
            var nameMatch = Regex.Match(valueRaw, @"'n':'([^']+)'", RegexOptions.IgnoreCase);
            var clubMatch = Regex.Match(valueRaw, @"'c':'([^']+)'", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                results.Add(new MarkerSearchResult
                {
                    Guid = match.Groups[1].Value,
                    Name = nameMatch.Success ? nameMatch.Groups[1].Value : "",
                    Club = clubMatch.Success ? clubMatch.Groups[1].Value : "",
                    Display = opt.InnerText.Trim()
                });
            }
        }

        return results;
    }
    
    public async Task<List<CourseWithTees>> FetchClubCoursesAndTeesAsync(string clubGuid)
{
    var scoreDateIso = DateTime.Now.ToString("yyyyMMddTHHmmss");
    var courseUrl = $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=GetCourses&ScoreDate={scoreDateIso}&Club_GUID={clubGuid}";
    Console.WriteLine($"📍 Fetching courses from: {courseUrl}");

    var response = await _httpClient.GetStringAsync(courseUrl);
    Console.WriteLine($"📦 Raw course response:\n{response}");

    using var jsonDoc = JsonDocument.Parse(response);
    var root = jsonDoc.RootElement;
    if (!root.TryGetProperty("Data", out var data))
        throw new Exception("❌ Could not find 'Data' property in course response.");

    var result = new List<CourseWithTees>();

    foreach (var course in data.EnumerateArray())
    {
        var courseName = course.GetProperty("Course_Name").GetString()?.Trim();
        var courseGuid = course.GetProperty("Course_GUID").GetString();

        Console.WriteLine($"📘 Course option: {courseName} => {courseGuid}");

        var teeUrl = $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=GetTees&ScoreDate={scoreDateIso}&Course_GUID={courseGuid}";
        Console.WriteLine($"📍 Fetching tees from: {teeUrl}");

        var teeJson = await _httpClient.GetStringAsync(teeUrl);
        Console.WriteLine($"📦 Raw tee response:\n{teeJson}");

        using var teeDoc = JsonDocument.Parse(teeJson);
        var teeRoot = teeDoc.RootElement;
        if (!teeRoot.TryGetProperty("Data", out var teeList))
            throw new Exception($"❌ JSON does not contain 'Data' field for tees on course {courseName}");

        var tees = new List<TeeOption>();
        foreach (var tee in teeList.EnumerateArray())
        {
            var name = tee.GetProperty("Text").GetString()?.Trim() ?? "";
            var gender = tee.GetProperty("Gender").GetString()?.Trim() ?? "";
            var guid = tee.GetProperty("Value").GetString();

            Console.WriteLine($"🏷️ Tee option: {name} ({gender}) => {guid}");

            tees.Add(new TeeOption
            {
                Name = name,
                Gender = gender,
                Guid = guid
            });
        }

        result.Add(new CourseWithTees
        {
            CourseName = courseName!,
            CourseGuid = courseGuid!,
            Tees = tees
        });
    }

    return result;
}

public class CourseWithTees
{
    public string CourseName { get; set; } = default!;
    public string CourseGuid { get; set; } = default!;
    public List<TeeOption> Tees { get; set; } = new();
}

public class TeeOption
{
    public string Name { get; set; } = default!;
    public string Gender { get; set; } = default!;
    public string Guid { get; set; } = default!;
}

}