public class CourseWithTees
{
    public string CourseName { get; set; } = default!;
    public string CourseGuid { get; set; } = default!;
    public List<TeeOption> Tees { get; set; } = new();
}