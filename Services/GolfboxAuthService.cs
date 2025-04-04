using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using GolfkollektivetBackend.Models;
using HtmlAgilityPack;
using System.Text.Json;

namespace GolfkollektivetBackend.Services;

public class GolfboxAuthService
{
    private readonly HttpClient _httpClient;

    public GolfboxAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(string? Hcp, string? SelectedGuid)> LoginAndGetHcpAndSelectedGuidAsync(string username,
        string password)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["loginform.submitted"] = "true",
            ["command"] = "login",
            ["loginform.rurl"] = "",
            ["loginform.username"] = username,
            ["loginform.password"] = password
        });
        
        var response = await _httpClient.PostAsync("https://www.golfbox.no/login.asp?rUrl=", content);
        if (!response.IsSuccessStatusCode)
            return (null, null);
        
        var frontPage = await _httpClient.GetStringAsync("https://www.golfbox.no/site/my_golfbox/myFrontPage.asp");
        if (!frontPage.Contains("GolfBox Player"))
            return (null, null);
        
        var hcpMatch = Regex.Match(frontPage, @"HCP[^0-9]*([0-9]{1,2},[0-9])");
        var hcp = hcpMatch.Success ? hcpMatch.Groups[1].Value : null;

        var selectedGuidMatch = Regex.Match(frontPage, @"newWHSScore\.asp\?selected=\{([A-F0-9\-]+)\}", 
            RegexOptions.IgnoreCase);
        
        var selectedGuid = selectedGuidMatch.Success ? $"{{{selectedGuidMatch.Groups[1].Value}}}" : null;
        
        Console.WriteLine(hcp != null
            ? $"📊 Detected HCP: {hcp}"
            : "⚠️ Could not extract HCP from front page.");

        Console.WriteLine(selectedGuid != null
            ? $"🧭 Detected Selected GUID: {selectedGuid}"
            : "⚠️ Could not extract Selected GUID from front page.");

        return (hcp, selectedGuid);
    }
    
    public async Task<(string PlayerGuid, string MagicName, string MagicValue, List<(string Name, string Guid)> Clubs)> GetDynamicTokenAsync(string selectedGuid)
    {
        var url = $"https://www.golfbox.no/site/my_golfbox/score/whs/newWHSScore.asp?selected={selectedGuid}";
        Console.WriteLine($"🌐 Fetching score form: {url}");

        var html = await _httpClient.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var inputs = doc.DocumentNode.SelectNodes("//input[@type='hidden']");
        if (inputs == null)
            throw new Exception("❌ No hidden input fields found!");

        string playerGuid = null;
        string magicName = null;
        string magicValue = null;

        foreach (var node in inputs)
        {
            var name = node.GetAttributeValue("name", "");
            var value = node.GetAttributeValue("value", "");
            // Console.WriteLine($"🔍 Hidden field: {name} = {value}");

            if (Regex.IsMatch(name, @"^[A-F0-9\-]{36}$", RegexOptions.IgnoreCase))
            {
                magicName = name;
                magicValue = value;
            }

            if (name == "fld_PlayerMemberGUID")
            {
                playerGuid = value;
            }
        }

        if (string.IsNullOrEmpty(playerGuid) || string.IsNullOrEmpty(magicName) || string.IsNullOrEmpty(magicValue))
            throw new Exception("❌ Required fields not found in the form");

        Console.WriteLine($"✅ Dynamic field: {magicName} = {magicValue}");
        Console.WriteLine($"✅ Player GUID: {playerGuid}");

        var clubs = new List<(string Name, string Guid)>();
        var clubOptions = doc.DocumentNode.SelectNodes("//select[@id='fld_Club']/option");
        if (clubOptions != null)
        {
            foreach (var option in clubOptions)
            {
                var name = option.InnerText.Trim();
                var guid = option.GetAttributeValue("value", "").Trim();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(guid))
                {
                    clubs.Add((name, guid));
                }
            }
        }

        return (playerGuid, magicName, magicValue, clubs);
    }
    
    public async Task<bool> SubmitPreparedScoreFormAsync(string selectedGuid, Dictionary<string, string> formData)
    {
        
        Console.WriteLine("📤 Submitting form data to GolfBox:");
        foreach (var kvp in formData)
        {
            Console.WriteLine($"{kvp.Key}: {kvp.Value}");
        }
        
        var response = await _httpClient.PostAsync(
            $"https://www.golfbox.no/site/my_golfbox/score/whs/newWHSScore.asp?selected={selectedGuid}",
            new FormUrlEncodedContent(formData));

        var body = await response.Content.ReadAsStringAsync();
        
        if (!body.Contains("Score er lagret") &&
            (response.Headers.Location?.ToString().Contains("listScoresToConfirm.asp") != true))
        {
            Console.WriteLine("❌ Submission HTML:\n" + body);
        }

        return body.Contains("Score er lagret") ||
               response.Headers.Location?.ToString().Contains("listScoresToConfirm.asp") == true;
    }
    

    public async Task LogoutAsync()
    {
        await _httpClient.GetAsync("https://www.golfbox.no/logoff.asp?sessiontimeout=1");
    }
    
}