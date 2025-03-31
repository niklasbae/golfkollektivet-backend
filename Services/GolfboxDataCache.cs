// Services/GolfboxDataCache.cs
using GolfkollektivetBackend.Models;
using System.Text.Json;

namespace GolfkollektivetBackend.Services;

public class GolfboxDataCache
{
    private readonly List<GolfboxClubData> _clubData = new();

    public void Save(List<GolfboxClubData> data)
    {
        _clubData.Clear();
        _clubData.AddRange(data);
    }

    public List<GolfboxClubData> Load()
    {
        return _clubData;
    }

    public string ExportAsJson()
    {
        return JsonSerializer.Serialize(_clubData, new JsonSerializerOptions { WriteIndented = true });
    }
}