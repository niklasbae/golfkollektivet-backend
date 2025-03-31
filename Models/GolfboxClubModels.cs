namespace GolfkollektivetBackend.Models;

public class GolfboxClubData
{
    public string ClubGuid { get; set; } = default!;
    public string ClubName { get; set; } = default!;
    public List<GolfboxCourseData> Courses { get; set; } = new();
}

public class GolfboxCourseData
{
    public string CourseGuid { get; set; } = default!;
    public string CourseName { get; set; } = default!;
    public List<GolfboxTeeData> Tees { get; set; } = new();
}

public class GolfboxTeeData
{
    public string TeeGuid { get; set; } = default!;
    public string TeeName { get; set; } = default!;
    public string TeeGender { get; set; } = default!;
}