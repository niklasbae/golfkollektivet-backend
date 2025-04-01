using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GolfkollektivetBackend.Models;
using Microsoft.Extensions.Configuration;

namespace GolfkollektivetBackend.Services;

public class ScorecardParserService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public ScorecardParserService(IConfiguration config)
    {
        _httpClient = new HttpClient();
        _config = config;
    }

    public async Task<ParsedScorecardResult?> ParseScorecardAsync(IFormFile imageFile)
    {
        try
        {
            // Convert image to base64
            await using var imageStream = imageFile.OpenReadStream();
            var base64Image = Convert.ToBase64String(await ReadAllBytesAsync(imageStream));

            // Prepare GPT-4o request
            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = @"Extract the player name, course name, and the hole scores from this golf scorecard. The score is always found under the column named 'Score'.

✅ Return valid JSON using these exact keys:
- playerName (string)
- courseName (string)
- holes (list of integers representing the hole-by-hole score, 9 or 18 entries)

🚨 Validation Rules (VERY IMPORTANT):
- The last number in the front 9 (often labeled 'Ut') is the total of the first 9 hole scores.
- The last number in the back 9 (often labeled 'In') is the total of the last 9 hole scores.
- There is also a final total score (e.g. Score 86/73 → 86 is raw score).
- You must **sum the hole scores** and confirm:
  - Front 9 total = sum of first 9 holes
  - Back 9 total = sum of last 9 holes
  - Final total = sum of all 18 scores

🧠 If the totals don’t match what’s printed, re-check the scores and try again until they do.

⚠️ Do NOT guess or skip holes — return exactly the hole values you can verify.
⚠️ Do NOT include any extra explanation. Output ONLY the JSON object.

Example format:

```json
{
  ""playerName"": ""Kim-Ole Myhre"",
  ""courseName"": ""Hovedbanen"",
  ""holes"": [4, 6, 6, 5, 5, 6, 5, 3, 4, 4, 5, 5, 4, 8, 4, 3, 4, 5]
}"
                            },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{imageFile.ContentType};base64,{base64Image}"
                                }
                            }
                        }
                    }
                },
                max_tokens = 1000
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var apiKey = _config["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Missing OpenAI API key in configuration.");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"OpenAI API returned {response.StatusCode}: {responseContent}");

            using var doc = JsonDocument.Parse(responseContent);
            var raw = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            Console.WriteLine("\n========== RAW GPT RESPONSE ==========\n");
            Console.WriteLine(raw);
            Console.WriteLine("\n======================================\n");

            if (string.IsNullOrWhiteSpace(raw))
                throw new Exception("OpenAI response message content was empty.");

            // Extract JSON from text
            var jsonStart = raw.IndexOf('{');
            var jsonEnd = raw.LastIndexOf('}');
            if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
                throw new Exception("Could not find JSON block in response:\n" + raw);

            var cleanedJson = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);

            var parsed = JsonSerializer.Deserialize<ParsedScorecardResult>(
                cleanedJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            
                        
            Console.WriteLine("CLEANED JSON:");
            Console.WriteLine(cleanedJson);
            
            // Apply defaults if missing
            parsed.ScoreDate ??= DateTime.Now.ToString("dd.MM.yyyy");
            parsed.ScoreTime ??= $"{DateTime.Now.Hour:00}:00";
            parsed.Holes ??= new List<int>();
            
            return parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScorecardParserService] Error parsing scorecard: {ex.Message}");
            return null;
        }
    }

    private async Task<byte[]> ReadAllBytesAsync(Stream input)
    {
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms);
        return ms.ToArray();
    }
}