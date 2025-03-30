// Models/SubmitScoreRequest.cs
using System.ComponentModel.DataAnnotations;

namespace GolfkollektivetBackend.Models;

public class SubmitScoreRequest
{
    [Required] public string Username { get; set; } = default!;
    [Required] public string Password { get; set; } = default!;

    [Required] public string ClubName { get; set; } = default!;
    [Required] public string CourseName { get; set; } = default!;
    [Required] public string TeeName { get; set; } = default!;
    [Required] public string MarkerName { get; set; } = default!;

    [Required, RegularExpression(@"\d{2}\.\d{2}\.\d{4}", ErrorMessage = "Format: dd.MM.yyyy")]
    public string ScoreDate { get; set; } = default!;

    [Required, RegularExpression(@"\d{2}:\d{2}", ErrorMessage = "Format: HH:mm")]
    public string ScoreTime { get; set; } = default!;

    [Required, MinLength(18), MaxLength(18)]
    public List<int> HoleScores { get; set; } = new();

    // Internal only, used after mapping
    public string? PlayerGuid { get; set; }
    public string? MarkerGuid { get; set; }
    public string? CourseGuid { get; set; }
    public string? TeeGuid { get; set; }
    public string? ClubId { get; set; }
    public string? SelectedGuid { get; set; }
}