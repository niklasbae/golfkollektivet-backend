namespace GolfkollektivetBackend.Models;

public class MarkerSearchResult
{
    public string Guid { get; set; } = default!;     // The marker's GUID
    public string Name { get; set; } = default!;     // Marker full name
    public string Club { get; set; } = default!;     // Home club
    public string Display { get; set; } = default!;  // Raw display text (e.g. "77-4183, Kim-Ole Myhre, Grønmo Golfklubb")
}