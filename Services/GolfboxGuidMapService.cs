// Services/GolfboxGuidMapService.cs
namespace GolfkollektivetBackend.Services;

public class GolfboxGuidMapService
{
    public string GetClubId(string clubName)
    {
        return clubName.ToLowerInvariant() switch
        {
            "onsøy" => "a85da1e0-b469-4702-bdbc-4e8972ec50a9",
            _ => throw new Exception($"Unknown club: {clubName}")
        };
    }

    public string GetCourseGuid(string clubName, string courseName)
    {
        return (clubName.ToLowerInvariant(), courseName.ToLowerInvariant()) switch
        {
            ("onsøy", "onsøy hovedbane") => "D82B1CBC-A2B8-4EA5-A9F8-8BE330234218",
            _ => throw new Exception($"Unknown course: {clubName} / {courseName}")
        };
    }

    public string GetTeeGuid(string clubName, string courseName, string teeName)
    {
        return (clubName.ToLowerInvariant(), courseName.ToLowerInvariant(), teeName) switch
        {
            ("onsøy", "onsøy hovedbane", "56") => "191A5956-B2A1-4ABD-BBB6-E5BAD796B5FD",
            _ => throw new Exception($"Unknown tee: {clubName} / {courseName} / {teeName}")
        };
    }
}