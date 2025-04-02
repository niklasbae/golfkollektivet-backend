namespace GolfkollektivetBackend.Models;

public class ForeignCourseRequest
{
    public string ClubName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string TeeName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}