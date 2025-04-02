using System.Text.Json.Serialization;

namespace GolfkollektivetBackend.Models;

public class StructuredHoleDataResponse
{
    [JsonPropertyName("holes")]
    public List<ForeignCourseHole> Holes { get; set; } = new();

    [JsonPropertyName("holeRow_start_y_coordinate")]
    public int HoleRowStartYCoordinate { get; set; }

    [JsonPropertyName("comments")]
    public string? Comments { get; set; }

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; set; }
}