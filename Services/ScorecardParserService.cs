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
            await using var imageStream = imageFile.OpenReadStream();
            var base64Image = Convert.ToBase64String(await ReadAllBytesAsync(imageStream));

            var courseData = File.ReadAllText("./Data/course-club-data.json");
            
            var promptText = $@"
Extract the players first name (usually Norwegian, then Scandinavian, lastly international), course name, and the hole scores from this golf scorecard. The score is always found under the column named 'Score'.

Below is a list of available clubs, courses, and tee names:

{courseData}

✅ Return valid JSON using these exact keys:
- playerName (string)
- gender (""Male"" or ""Female"", best guess)
- clubName (string)
- courseName (string)
- holes (list of integers representing the hole-by-hole score, 9 or 18 entries)

🚨 VALIDATION RULES (EXTREMELY IMPORTANT):

✅ You must extract the hole-by-hole scores (9 or 18 values) and confirm the following:
- The **Front 9 total** (often labeled ""Ut"") = sum of holes 1–9.
- The **Back 9 total** (often labeled ""In"") = sum of holes 10–18.
- The **Final score** = sum of all 18 holes.

💡 These totals are always printed somewhere on the scorecard. Use them to double-check the hole values.

🧠 If the sum does NOT match the printed total:
- Re-read the full image, but start from hole 18
- Go over 1 digit at a time, and calculate a certainty score for each. Fix then only the one with lowest certainty, and re-do the validation. If two digits have low certainty, fix both. If the new certainty is lower than initially, redo the reading.
- Count how many re-reads you had and output it

🚫 DO NOT GUESS. Return only verified hole values.
🚫 DO NOT merge `clubName` and `courseName`. Keep them as separate fields.
🚫 DO NOT return any hole if the value cannot be confidently extracted — exclude it and explain which one is missing.

✅ OUTPUT must be a **pure JSON object** with keys:
- `playerName` (string)
- `gender` (""Male"" or ""Female"")
- `clubName` (string)
- `courseName` (string)
- `holes` (array of integers)
- `number of re-reads` (string)

⚠️ Output ONLY a valid JSON object. No explanations, no markdown.
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
                            new { type = "image_url", image_url = new { url = $"data:{imageFile.ContentType};base64,{base64Image}" } }
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

    private async Task<byte[]> ReadAllBytesAsync(Stream input)
    {
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms);
        return ms.ToArray();
    }
}