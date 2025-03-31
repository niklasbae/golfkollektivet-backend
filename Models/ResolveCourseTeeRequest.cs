namespace GolfkollektivetBackend.Models;

public class ResolveCourseTeeRequest
{
    public string ClubGuid { get; set; } = default!;
    public string CourseName { get; set; } = default!;
    public string TeeName { get; set; } = default!;
    public string TeeGender { get; set; } = "Male";
    public string ScoreDate { get; set; } = default!; // Format: dd.MM.yyyy
    public string ScoreTime { get; set; } = default!; // Format: HH:mm
}