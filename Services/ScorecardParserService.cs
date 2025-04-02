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
            //var courseData = File.ReadAllText("./Data/course-club-data.json");
            var promptText = BuildPromptText();
            var requestBody = CreateRequestBody(promptText, enhancedBase64, imageFile.ContentType);
            var apiKey = _config["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Missing OpenAI API key in configuration.");

            var response = await SendOpenAIRequest(requestBody, apiKey);
            var rawJson = ExtractRawJsonFromResponse(response);
            Console.WriteLine("CLEANED JSON:\n" + rawJson);

            var parsed = JsonSerializer.Deserialize<ParsedScorecardResult>(rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            AssignDefaults(parsed);
            //await MatchCourseAndTeeAsync(parsed, courseData);

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
            image.Mutate(ctx =>
            {
                ctx.AutoOrient()
                    .Grayscale()
                    .Brightness(1.15f) // Slight boost for faded digits
                    .Contrast(1.3f) // Sharpen separation from background
                    .GaussianSharpen(1.3f); // Clarify character edges
            });
        });

        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private string BuildPromptText()
    {
        return $@"
Extract golf scores from the 'Score' column in the provided scorecard image. Return scores as a JSON object with a key 'holes' containing a list of integers. Ensure the sum of the first 9 scores matches the 'Ut' total and the last 9 scores match the 'In' total. 
If discrepancies arise, start corrections from hole 18 and recheck each step for accuracy. Include a 'comments' field if validation issues persist, and give me the confidence score. If confidence score is below 0.93, recheck.
";
        
        
        
        
        
        
    }

    private object CreateRequestBody(string promptText, string base64Image, string contentType) => new
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
                    new { type = "image_url", image_url = new { url = $"data:{contentType};base64,{base64Image}" } }
                }
            }
        },
        max_tokens = 1200
    };

    private async Task<string> SendOpenAIRequest(object requestBody, string apiKey)
    {
        var jsonContent = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"OpenAI API returned {response.StatusCode}: {responseContent}");

        return responseContent;
    }

    private string ExtractRawJsonFromResponse(string responseContent)
    {
        using var doc = JsonDocument.Parse(responseContent);
        var raw = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        Console.WriteLine("\n========== RAW GPT RESPONSE ==========" + raw + "\n======================================\n");

        if (string.IsNullOrWhiteSpace(raw))
            throw new Exception("OpenAI response message content was empty.");

        var jsonStart = raw.IndexOf('{');
        var jsonEnd = raw.LastIndexOf('}');
        if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
            throw new Exception("Could not find JSON block in response.");

        return raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
    }

    private void AssignDefaults(ParsedScorecardResult parsed)
    {
        parsed.ScoreDate ??= DateTime.Now.ToString("dd.MM.yyyy");
        parsed.ScoreTime ??= $"{DateTime.Now.Hour:00}:00";
        parsed.Holes ??= new List<int>();
    }

    private async Task MatchCourseAndTeeAsync(ParsedScorecardResult parsed, string courseData)
    {
        try
        {
            var golfboxClubs = JsonSerializer.Deserialize<List<GolfboxClubData>>(courseData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var matchedClub = golfboxClubs?.FirstOrDefault(club => string.Equals(club.ClubName.Trim(), parsed.ClubName?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matchedClub == null)
            {
                Console.WriteLine($"[ScorecardParserService] Could not match club: {parsed.ClubName}");
                return;
            }

            Console.WriteLine($"[ScorecardParserService] Matched club: {matchedClub.ClubName}");

            var matchedCourse = matchedClub.Courses.FirstOrDefault(course => string.Equals(course.CourseName.Trim(), parsed.CourseName?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matchedCourse == null)
            {
                Console.WriteLine($"[ScorecardParserService] Could not match course: {parsed.CourseName} in club: {matchedClub.ClubName}");
                return;
            }

            var matchingTees = matchedCourse.Tees
                .Where(t => string.Equals(t.TeeGender, parsed.Gender, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => int.TryParse(t.TeeName, out var len) ? len : 0)
                .ToList();

            var selectedTeeName = parsed.Gender == "Male"
                ? matchingTees.ElementAtOrDefault(1)?.TeeName
                : matchingTees.ElementAtOrDefault(2)?.TeeName;

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
        catch (Exception ex)
        {
            Console.WriteLine($"[ScorecardParserService] Error while assigning tee name: {ex.Message}");
        }
    }
}
