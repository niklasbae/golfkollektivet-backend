using GolfkollektivetBackend.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace GolfkollektivetBackend.Services;

public class GolfboxMarkerService
{
    private readonly HttpClient _httpClient;

    public GolfboxMarkerService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<MarkerSearchResult>> SearchAsync(string input)
{
    var isMedlemsnummer = Regex.IsMatch(input, @"^\d{1,9}-\d{1,9}$");

    if (isMedlemsnummer)
    {
        // GET request for medlemsnummer
        var url = $"https://www.golfbox.no/site/my_golfbox/score/whs/_searchMember.asp?id={input}&country=NO";
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        // Empty or invalid response
        if (string.IsNullOrWhiteSpace(content) || !content.Contains("|"))
            return new List<MarkerSearchResult>();

        // Response format: {GUID}|Name|Club|... (we only care about first 3)
        var parts = content.Split('|');
        if (parts.Length < 3)
            return new List<MarkerSearchResult>();

        return new List<MarkerSearchResult>
        {
            new()
            {
                Guid = parts[0],
                Name = parts[1],
                Club = parts[2],
                Display = $"{input}, {parts[1]}, {parts[2]}"
            }
        };
    }
    else
    {
        // POST request for name search
        var response = await _httpClient.PostAsync(
            "https://www.golfbox.no/site/my_golfbox/score/whs/_searchMember.asp",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = input,
                ["country"] = "NO"
            })
        );

        var html = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var options = doc.DocumentNode.SelectNodes("//select[@id='slc_MarkerSearch4result']/option");

        var results = new List<MarkerSearchResult>();
        if (options == null)
            return results;

        foreach (var opt in options)
        {
            var valueRaw = opt.GetAttributeValue("value", "");

            var guidMatch = Regex.Match(valueRaw, @"'g':'(\{[A-F0-9\-]+\})'", RegexOptions.IgnoreCase);
            var nameMatch = Regex.Match(valueRaw, @"'n':'([^']+)'", RegexOptions.IgnoreCase);
            var clubMatch = Regex.Match(valueRaw, @"'c':'([^']+)'", RegexOptions.IgnoreCase);

            if (guidMatch.Success)
            {
                results.Add(new MarkerSearchResult
                {
                    Guid = guidMatch.Groups[1].Value,
                    Name = nameMatch.Success ? nameMatch.Groups[1].Value : "",
                    Club = clubMatch.Success ? clubMatch.Groups[1].Value : "",
                    Display = opt.InnerText.Trim()
                });
                Console.WriteLine("Markør funnet: " + nameMatch.Groups[1].Value);
            }
        }
        
        Console.WriteLine("Markør funnet: ");

        return results;
    }
}

    public async Task<string?> GetMarkerGuidAsync(string name)
    {
        var results = await SearchAsync(name);
        return results.FirstOrDefault()?.Guid;
    }
}