using GolfkollektivetBackend.Models;

namespace GolfkollektivetBackend.Services;

public class GolfboxDataSeeder
{
    private readonly GolfboxCourseService _courseService;
    private readonly GolfboxDataCache _cache;

    public GolfboxDataSeeder(GolfboxCourseService courseService, GolfboxDataCache cache)
    {
        _courseService = courseService;
        _cache = cache;
    }

    public async Task<List<GolfboxClubData>> FetchAndCacheAllClubsAsync(List<FetchClubDataRequest.ClubInput> clubs)
    {
        var data = new List<GolfboxClubData>();

        foreach (var club in clubs)
        {
            Console.WriteLine($"\n⛳ Fetching data for club: {club.Name} ({club.Guid})");

            try
            {
                var courses = await _courseService.FetchClubCoursesAndTeesAsync(club.Guid);

                // Correctly map CourseWithTees to GolfboxCourseData
                data.Add(new GolfboxClubData 
                { 
                    ClubName = club.Name, 
                    ClubGuid = club.Guid, 
                    Courses = courses.Select(course => new GolfboxCourseData
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
                });
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
}