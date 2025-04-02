using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GolfkollektivetBackend.Models;

namespace GolfkollektivetBackend.Services;

public class ForeignCourseDataService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public ForeignCourseDataService(IConfiguration config)
    {
        _httpClient = new HttpClient();
        _config = config;
    }

    public async Task<ForeignCourseData?> GetCourseDataAsync(string clubName, string courseName, string teeName, string country)
    {
        var prompt = BuildPrompt(clubName, courseName, teeName, country);
        Console.WriteLine("📝 GPT PROMPT:\n" + prompt);

        var requestBody = new
        {
            model = "gpt-4o",
            messages = new[]
            {
                new {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt }
                    }
                }
            },
            max_tokens = 1500
        };

        var json = JsonSerializer.Serialize(requestBody);
        Console.WriteLine("📦 REQUEST BODY:\n" + json);

        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("❌ Missing OpenAI API key.");
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine("📨 RAW GPT RESPONSE:\n" + content);

        var jsonFromMarkdown = ExtractJsonFromMarkdown(content);
        if (string.IsNullOrWhiteSpace(jsonFromMarkdown))
        {
            Console.WriteLine("❌ Failed to extract JSON block from content.");
            return null;
        }

        Console.WriteLine("✅ CLEANED JSON:\n" + jsonFromMarkdown);

        try
        {
            var parsed = JsonSerializer.Deserialize<ForeignCourseData>(jsonFromMarkdown, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed == null)
            {
                Console.WriteLine("❌ Deserialization returned null.");
            }
            else
            {
                Console.WriteLine($"✅ Parsed course: {courseName}, Tee: {teeName}, Par: {parsed.CoursePar}, CR: {parsed.CourseRating}, Slope: {parsed.Slope}");
                Console.WriteLine($"⛳ Holes count: {parsed.Holes?.Count ?? 0}");
            }
            
            AssignDefaults(parsed, courseName, teeName);
            
            return parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Deserialization error: {ex.Message}");
            return null;
        }
    }
    
    private void AssignDefaults(ForeignCourseData parsed, string courseName, string teeName)
    {
        parsed.ManualCourseName = courseName;
        parsed.ManualTee = teeName;
        parsed.Holes ??= new List<ForeignCourseHole>();
        parsed.Website = parsed.Website?.Trim();
        parsed.Note = parsed.Note?.Trim();
    }

    private string ExtractJsonFromMarkdown(string fullResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(fullResponse);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return "";

            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
                return "";

            return content.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to parse GPT response structure: {ex.Message}");
            return "";
        }
    }

    private string BuildPrompt(string clubName, string courseName, string teeName, string country)
    {
        return $@"
Return the following data as valid JSON only — no explanation or markdown formatting.

Input:
- Golf Club: {clubName}
- Course: {courseName}
- Tee: {teeName}
- Country: {country}

Make sure to use all fields of the input for max accuracy.

Output JSON structure:
{{
  ""coursePar"": int,
  ""courseRating"": decimal,
  ""slope"": int,
  ""holes"": [
    {{
      ""holeNumber"": 1,
      ""par"": int,
      ""hcp"": int
    }},
    ...
    {{
      ""holeNumber"": 18,
      ""par"": int,
      ""hcp"": int
    }}
  ],
    ""website"": ""https://...""
}}

If the data is incomplete or uncertain, include ""note"": ""brief explanation or source, making no question about wether data is 100% correct or approximate"" at the end of the JSON. **If data is approximate, leave the holes list empty.** 
- If available, the **official golf club or course website** as `""website"": ""https://...""`

";
    }
}