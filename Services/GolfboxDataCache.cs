// Services/GolfboxDataCache.cs
using GolfkollektivetBackend.Models;
using System.Text.Json;

namespace GolfkollektivetBackend.Services;

public class GolfboxDataCache
{
    private readonly List<GolfboxClubData> _clubData = new();
    private const string CacheFilePath = "./Data/golfbox-cache.json";

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

    public void LoadFromDisk()
    {
        if (!File.Exists(CacheFilePath))
        {
            Console.WriteLine("⚠️ golfbox-cache.json not found. Skipping preload.");
            return;
        }

        try
        {
            var json = File.ReadAllText(CacheFilePath);
            var parsed = JsonSerializer.Deserialize<List<GolfboxClubData>>(json);

            if (parsed is { Count: > 0 })
            {
                _clubData.Clear();
                _clubData.AddRange(parsed);
                RemoveDuplicateTees();
                Console.WriteLine($"✅ Loaded {parsed.Count} clubs from golfbox-cache.json");
            }
            else
            {
                Console.WriteLine("⚠️ No data in cache file.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to read golfbox-cache.json: {ex.Message}");
        }
    }
    
    private void RemoveDuplicateTees()
    {
        foreach (var club in _clubData)
        {
            foreach (var course in club.Courses)
            {
                course.Tees = course.Tees
                    .GroupBy(t => t.TeeGuid)
                    .Select(g =>
                        g.FirstOrDefault(t => t.TeeGender.Equals("Male", StringComparison.OrdinalIgnoreCase)) ??
                        g.First()
                    )
                    .ToList();
            }
        }

        Console.WriteLine("🧹 Removed duplicate tee GUIDs (kept Male if both existed).");
    }
}
