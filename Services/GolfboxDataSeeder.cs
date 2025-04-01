using System.Text.Json;
using System.Text.RegularExpressions;
using GolfkollektivetBackend.Models;

namespace GolfkollektivetBackend.Services;

public class GolfboxDataSeeder
{
    private readonly GolfboxCourseService _courseService;
    private readonly GolfboxDataCache _cache;

    private const string ClubInputFilePath = "./Data/manual-clubs.json";
    private const string HtmlInputFilePath = "./Data/manual-clubs-html";

    public GolfboxDataSeeder(GolfboxCourseService courseService, GolfboxDataCache cache)
    {
        _courseService = courseService;
        _cache = cache;
    }

    public async Task<List<GolfboxClubData>> FetchAndCacheAllClubsAsync(List<FetchClubDataRequest.ClubInput> clubs)
    {
        if (File.Exists(HtmlInputFilePath))
        {
            Console.WriteLine($"📄 Parsing clubs from HTML file: {HtmlInputFilePath}");
        
            var html = await File.ReadAllTextAsync(HtmlInputFilePath);
            
            // 🔧 Fix raw HTML before parsing
            html = html.Replace(" selected", ""); 
        
            clubs = ParseHtmlOptions(html);
        }
        else if (File.Exists(ClubInputFilePath))
        {
            Console.WriteLine($"📁 Using clubs from local file: {ClubInputFilePath}");

            var json = await File.ReadAllTextAsync(ClubInputFilePath);

            try
            {
                var parsed = JsonSerializer.Deserialize<ClubFileModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed?.Clubs is { Count: > 0 })
                    clubs = parsed.Clubs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to read local club file: {ex.Message}");
            }
        }

        var data = new List<GolfboxClubData>();

        foreach (var club in clubs)
        {
            Console.WriteLine($"\n⛳ Fetching data for club: {club.Name} ({club.Guid})");

            try
            {
                var courses = await _courseService.FetchClubCoursesAndTeesAsync(club.Guid);
                
                data.Add(new GolfboxClubData
                {
                    ClubName = club.Name,
                    ClubGuid = club.Guid,
                    Courses = courses.Select(course => new GolfboxCourseData
                    {
                        CourseName = course.CourseName,
                        CourseGuid = course.CourseGuid,
                        Tees = course.Tees
                            .GroupBy(tee => tee.Guid)
                            .Select(g =>
                                g.FirstOrDefault(t => t.Gender.Equals("Male", StringComparison.OrdinalIgnoreCase)) ??
                                g.First()
                            )
                            .Select(tee => new GolfboxTeeData
                            {
                                TeeName = tee.Name,
                                TeeGuid = tee.Guid,
                                TeeGender = tee.Gender
                            }).ToList()
                    }).ToList()
                });
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed for {club.Name}: {ex.Message}");
            }
        }

        _cache.Save(data);
        Console.WriteLine("✅ All data fetched and cached.");
        return data;
    }

    private List<FetchClubDataRequest.ClubInput> ParseHtmlOptions(string html)
    {
        var matches = Regex.Matches(html, "<option value=\"(.*?)\">(.*?)<\\/option>");

        return matches.Select(m => new FetchClubDataRequest.ClubInput
        {
            Guid = m.Groups[1].Value.Trim(),
            Name = m.Groups[2].Value.Trim()
        }).ToList();
    }

    private class ClubFileModel
    {
        public List<FetchClubDataRequest.ClubInput> Clubs { get; set; } = new();
    }
}
