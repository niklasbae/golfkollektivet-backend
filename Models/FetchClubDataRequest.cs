namespace GolfkollektivetBackend.Models;

public class FetchClubDataRequest
{
    public List<ClubInput> Clubs { get; set; } = new();

    public class ClubInput
    {
        public string Name { get; set; } = default!;
        public string Guid { get; set; } = default!;
    }
}