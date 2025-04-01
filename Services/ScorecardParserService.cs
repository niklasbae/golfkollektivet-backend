using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GolfkollektivetBackend.Models;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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
            var enhancedBase64 = await EnhanceAndEncodeImageAsync(imageFile);
            
            var courseData = File.ReadAllText("./Data/course-club-data.json");
            
            var promptText = $@"
Extract the player's **first name**, **gender**, **club name**, **course name**, and **hole-by-hole scores** from this golf scorecard image. Scores are always under a column labeled 'Score'.

🏌️ Below is the list of valid golf clubs, courses, and tees:

{{courseData}}

📥 Return a valid JSON object with these **exact keys**:
- `playerName` (string, first name only)
- `gender` (""Male"" or ""Female"", best guess)
- `clubName` (must match from list above)
- `courseName` (must match from list above)
- `holes` (list of 9 or 18 integers)

🧠 **Validation rules:**
- Sum of first 9 holes = ""Front 9"" total (labeled 'Ut')
- Sum of last 9 holes = ""Back 9"" total (labeled 'In')
- Sum of all = final total score (e.g., 86 in ""86/72"")

🔍 If the sums don’t match:
- Start from hole 18 and re-check values.
- Only correct scores with **low certainty**, one at a time.
- Rerun validation after each fix.
- If still uncertain, return a `""comments""` field explaining what couldn't be validated.

🚫 Do NOT guess, skip, or merge club/course names.
🚫 Do NOT invent names — everything must come from the list above.

✅ Output a **pure JSON object only**, like this:
```json
{{
  ""playerName"": ""Kim-Ole"",
  ""gender"": ""Male"",
  ""clubName"": ""Gamle Fredrikstad Golfklubb"",
  ""courseName"": ""Hovedbane Gamle Fredrikstad GK"",
  ""holes"": [4, 6, 6, 5, 5, 5, 6, 5, 3, 4, 5, 5, 4, 8, 4, 3, 4, 5]
}}
";

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
                            new { type = "text", text = promptText },
                            new { type = "image_url", image_url = new { url = $"data:image/png;base64,{enhancedBase64}" } }
                        }
                    }
                },
                max_tokens = 1200
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

            var jsonStart = raw.IndexOf('{');
            var jsonEnd = raw.LastIndexOf('}');
            if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
                throw new Exception("Could not find JSON block in response.");

            var cleanedJson = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
            Console.WriteLine("CLEANED JSON:");
            Console.WriteLine(cleanedJson);

            var parsed = JsonSerializer.Deserialize<ParsedScorecardResult>(
                cleanedJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            parsed.ScoreDate ??= DateTime.Now.ToString("dd.MM.yyyy");
            parsed.ScoreTime ??= $"{DateTime.Now.Hour:00}:00";
            parsed.Holes ??= new List<int>();

            if (!string.IsNullOrWhiteSpace(parsed.CourseName) && !string.IsNullOrWhiteSpace(parsed.Gender))
            {
                try
                {
                    var golfboxClubs = JsonSerializer.Deserialize<List<GolfboxClubData>>(courseData, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var allCourses = golfboxClubs?
                        .SelectMany(club => club.Courses)
                        .ToList();

                    Console.WriteLine($"[ScorecardParserService] Trying to match course name: '{parsed.CourseName.Trim()}'");
                    Console.WriteLine("[ScorecardParserService] Available course names:");

                    if (allCourses == null || !allCourses.Any())
                    {
                        Console.WriteLine("[ScorecardParserService] No courses found or allCourses is null.");
                    }
                    else
                    {
                        foreach (var course in allCourses)
                        {
                            Console.WriteLine($"- '{course.CourseName.Trim()}'");
                        }
                    }

                    var matchedCourse = allCourses?
                        .FirstOrDefault(course =>
                            string.Equals(course.CourseName.Trim(), parsed.CourseName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (matchedCourse != null)
                    {
                        var matchingTees = matchedCourse.Tees
                            .Where(t => string.Equals(t.TeeGender, parsed.Gender, StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(t => int.TryParse(t.TeeName, out var len) ? len : 0)
                            .ToList();

                        var selectedTeeName = parsed.Gender == "Male"
                            ? matchingTees.ElementAtOrDefault(1)?.TeeName  // 2nd longest
                            : matchingTees.ElementAtOrDefault(2)?.TeeName; // 3rd longest

                        if (!string.IsNullOrWhiteSpace(selectedTeeName))
                        {
                            parsed.TeeName = selectedTeeName;
                            Console.WriteLine($"[ScorecardParserService] Selected tee '{selectedTeeName}' for gender '{parsed.Gender}' on course '{parsed.CourseName}'");
                        }
                        else
                        {
                            Console.WriteLine($"[ScorecardParserService] Could not find suitable tee for gender '{parsed.Gender}' on course '{parsed.CourseName}'");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[ScorecardParserService] Could not match course: {parsed.CourseName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScorecardParserService] Error while assigning tee name: {ex.Message}");
                }
            }

            return parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScorecardParserService] Error parsing scorecard: {ex.Message}");
            return null;
        }
    }

    private async Task<string> EnhanceAndEncodeImageAsync(IFormFile imageFile)
    {
        using var inputStream = imageFile.OpenReadStream();
        using var image = await Image.LoadAsync(inputStream);

        image.Mutate(ctx =>
        {
            ctx
                .Resize(new ResizeOptions
                {
                    Size = new Size(image.Width * 2, image.Height * 2),
                    Mode = ResizeMode.Max
                })
                .AutoOrient()
                .Grayscale()
                .Contrast(1.2f)
                .GaussianSharpen();
        });

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }
    
    private async Task<byte[]> ReadAllBytesAsync(Stream input)
    {
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms);
        return ms.ToArray();
    }
}