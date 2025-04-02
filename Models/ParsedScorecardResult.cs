using System.Text.Json.Serialization;

namespace GolfkollektivetBackend.Models;

public class ParsedScorecardResult
{
    public string? PlayerName { get; set; }
    public string? ClubName { get; set; }
    public string? CourseName { get; set; }
    public string? TeeName { get; set; }
    public string? ScoreDate { get; set; }
    public string? ScoreTime { get; set; }
    public List<int> Holes { get; set; } = new();
    public string? Gender { get; set; }
    [JsonPropertyName("holeRow_start_y_coordinate")]
    public int? HoleRowStartYCoordinate { get; set; }
    
    public List<ForeignCourseHole> HoleDetails { get; set; } = new();

}