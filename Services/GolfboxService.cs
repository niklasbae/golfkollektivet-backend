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

    public async Task<(string PlayerGuid, string MagicName, string MagicValue)> GetDynamicTokenAsync(string selectedGuid)
    {
        var url = $"https://www.golfbox.no/site/my_golfbox/score/whs/newWHSScore.asp?selected={selectedGuid}";
        Console.WriteLine($"🌐 Fetching score form: {url}");

        var html = await _httpClient.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var inputs = doc.DocumentNode.SelectNodes("//input[@type='hidden']");
        if (inputs == null)
            throw new Exception("❌ No hidden input fields found!");

        foreach (var node in inputs)
        {
            var name = node.GetAttributeValue("name", "");
            var value = node.GetAttributeValue("value", "");
            Console.WriteLine($"🔍 Hidden field: {name} = {value}");
        }

        var uuidInput = inputs.FirstOrDefault(n =>
            Regex.IsMatch(n.GetAttributeValue("name", ""), @"^[A-F0-9\-]{36}$", RegexOptions.IgnoreCase));

        if (uuidInput == null)
            throw new Exception("❌ Dynamic UUID input field not found");

        var magicName = uuidInput.GetAttributeValue("name", "");
        var magicValue = uuidInput.GetAttributeValue("value", "");

        var playerGuidInput = inputs.FirstOrDefault(n =>
            n.GetAttributeValue("name", "") == "fld_PlayerMemberGUID");

        if (playerGuidInput == null)
            throw new Exception("❌ Player GUID input field not found");

        var playerGuid = playerGuidInput.GetAttributeValue("value", "");

        Console.WriteLine($"✅ Dynamic field: {magicName} = {magicValue}");
        Console.WriteLine($"✅ Player GUID: {playerGuid}");

        return (playerGuid, magicName, magicValue);
    }

    public async Task<(string Par, string Rating, string Slope, string Pcc)> FetchCourseStatsAsync(
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

        Console.WriteLine($"📐 Course stats: Par={par}, CR={crFormatted}, Slope={slope}, PCC={pcc}");

        return (par, crFormatted, slope, pcc);
    }

    public async Task<bool> SubmitScoreAsync(SubmitScoreRequest req, string magicName, string magicValue)
    {
        var stats = await FetchCourseStatsAsync(req.CourseGuid!, req.TeeGuid!, req.PlayerGuid!, req.ScoreDate);

        var formData = new Dictionary<string, string>
        {
            ["selected"] = req.SelectedGuid,
            ["command"] = "save",
            [magicName] = magicValue,
            ["rUrl"] = "/site/my_golfbox/score/whs/newWHSScore.asp",
            ["isHcpQualifying"] = "1",
            ["fld_PlayerMemberGUID"] = req.PlayerGuid,
            ["chk_IsCounting"] = "on",
            ["fld_MemberGUID"] = req.PlayerGuid,
            ["fld_ScoreDate"] = req.ScoreDate,
            ["fld_ScoreTime"] = req.ScoreTime,
            ["rdo_RoundType"] = "2",
            ["fld_HolesPlayed"] = req.HoleScores.Count.ToString(),
            ["fld_Club"] = req.ClubId,
            ["fld_PCC"] = stats.Pcc,
            ["fld_Course"] = req.CourseGuid,
            ["fld_Tee"] = req.TeeGuid,
            ["fld_CoursePar"] = stats.Par,
            ["fld_CourseRating"] = stats.Rating,
            ["fld_Slope"] = stats.Slope,
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
}