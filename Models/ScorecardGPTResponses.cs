using System.Text.Json.Serialization;

namespace GolfkollektivetBackend.Models;

public class ScoreResponse
{
    [JsonPropertyName("holesScores")]
    public List<int> HolesScores { get; set; } = new();
}

public class HcpResponse
{
    [JsonPropertyName("holesHcp")]
    public List<int> HolesHcp { get; set; } = new();
}

public class ParResponse
{
    [JsonPropertyName("holesPar")]
    public List<int> HolesPar { get; set; } = new();
}