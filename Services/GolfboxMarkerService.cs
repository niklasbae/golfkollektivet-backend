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

    public async Task<List<MarkerSearchResult>> SearchAsync(string name)
    {
        var response = await _httpClient.PostAsync(
            "https://www.golfbox.no/site/my_golfbox/score/whs/_searchMember.asp",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = name,
                ["country"] = "NO"
            })
        );

        var html = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var options = doc.DocumentNode.SelectNodes("//select[@id='slc_MarkerSearch4result']/option");
        if (options == null) 
            return new List<MarkerSearchResult>();

        var results = new List<MarkerSearchResult>();

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
            }
        }

        return results;
    }

    public async Task<string?> GetMarkerGuidAsync(string name)
    {
        var results = await SearchAsync(name);
        return results.FirstOrDefault()?.Guid;
    }
}