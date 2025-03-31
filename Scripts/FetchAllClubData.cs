
// Scripts/FetchAllClubData.cs
using GolfkollektivetBackend.Services;
using GolfkollektivetBackend.Models;
using System.Text.Json;

public class FetchAllClubData
{
    private readonly GolfboxService _golfboxService;
    private readonly GolfboxDataCache _cache;

    public FetchAllClubData(GolfboxService golfboxService, GolfboxDataCache cache)
    {
        _golfboxService = golfboxService;
        _cache = cache;
    }

    public async Task<List<GolfboxClubData>> FetchAndCacheAllClubsAsync(List<(string Name, string Guid)> clubs)
    {
        var allClubData = new List<GolfboxClubData>();

        foreach (var (name, guid) in clubs)
        {
            Console.WriteLine($"\n⛳ Fetching data for club: {name} ({guid})");

            try
            {
                var coursesWithTees = await _golfboxService.FetchClubCoursesAndTeesAsync(guid);

                var clubData = new GolfboxClubData
                {
                    ClubName = name,
                    ClubGuid = guid,
                    Courses = coursesWithTees.Select(course => new GolfboxCourseData
                    {
                        CourseName = course.CourseName,
                        CourseGuid = course.CourseGuid,
                        Tees = course.Tees.Select(tee => new GolfboxTeeData
                        {
                            TeeName = tee.Name,
                            TeeGuid = tee.Guid,
                            TeeGender = tee.Gender
                        }).ToList()
                    }).ToList()
                };

                allClubData.Add(clubData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed for {name}: {ex.Message}");
            }
        }

        _cache.Save(allClubData);
        Console.WriteLine("✅ All data fetched and cached.");

        return allClubData;
    }
}