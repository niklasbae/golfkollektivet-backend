using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using GolfkollektivetBackend.Models;
using HtmlAgilityPack;
using System.Text.Json;

namespace GolfkollektivetBackend.Services;

public class GolfboxCourseService
{
    private readonly HttpClient _httpClient;

    public GolfboxCourseService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
    
    public async Task<(string Par, string Rating, string Slope, string Pcc, string IsHcpQualifying)>
        FetchCourseStatsAsync(
            string courseGuid,
            string teeGuid,
            string playerGuid,
            string date)
    {
        var formattedDate = DateTime.ParseExact(date, "dd.MM.yyyy", null).ToString("yyyyMMddTHHmmss");

        var url =
            $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=UpdateStats&ScoreDate={formattedDate}&Course_GUID={courseGuid}&Member_GUID={playerGuid}&Tee_GUID={teeGuid}";
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

        Console.WriteLine(
            $"📐 Course stats: Par={par}, CR={crFormatted}, Slope={slope}, PCC={pcc}, IsHCPQualifying={isHcpQualifying}");

        return (par, crFormatted, slope, pcc, isHcpQualifying);
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

    public async Task<string> ResolveCourseGuidAsync(
        string clubGuid,
        string courseName,
        string scoreDate,
        string scoreTime)
    {
        var dateTime = DateTime.ParseExact($"{scoreDate} {scoreTime}", "dd.MM.yyyy HH:mm", null);
        var scoreDateIso = dateTime.ToString("yyyyMMddTHHmmss");

        var url =
            $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=GetCourses&ScoreDate={scoreDateIso}&Club_GUID={clubGuid}";
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
        var url =
            $"https://www.golfbox.no/site/score/whs/api/serviceCaller.asp?action=GetTees&ScoreDate={scoreDate}&Course_GUID={courseGuid}";
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
            throw new Exception(
                $"❌ Tee '{teeName}' ({teeGender}) not found for course {courseGuid}. Available: {fallback}");
        }

        return teeGuid;
    }
    
    public async Task<ResolveCourseTeeResult> ResolveCourseAndTeeAsync(ResolveCourseTeeRequest request)
    {
        try
        {
            var courseGuid = await ResolveCourseGuidAsync(request.ClubGuid, request.CourseName, request.ScoreDate, request.ScoreTime);
            var teeGuid = await ResolveTeeGuidAsync(courseGuid, request.TeeName, request.TeeGender);

            return new ResolveCourseTeeResult
            {
                Success = true,
                CourseGuid = courseGuid,
                TeeGuid = teeGuid
            };
        }
        catch (Exception ex)
        {
            return new ResolveCourseTeeResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }


    
}