using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GolfkollektivetBackend.Models;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
            var promptText = BuildScorePromptText();

            var requestBody = CreateRequestBody(promptText, enhancedBase64, imageFile.ContentType);
            var apiKey = _config["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new Exception("Missing OpenAI API key in configuration.");

            var response = await SendOpenAIRequest(requestBody, apiKey);
            var rawJson = ExtractRawJsonFromResponse(response);
            Console.WriteLine("CLEANED JSON:\n" + rawJson);

            var scoreData = JsonSerializer.Deserialize<ScoreResponse>(rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (scoreData == null)
                throw new Exception("Failed to parse GPT response for scorecard.");

            var parsed = new ParsedScorecardResult
            {
                Holes = scoreData.HolesScores
            };

            AssignDefaults(parsed);
            return parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScorecardParserService] Error parsing scorecard: {ex.Message}");
            return null;
        }
    }
    
    public async Task<ParsedScorecardResult?> ParseScorecardStructuredHoleDataParallelAsync(IFormFile imageFile)
{
    try
    {
        var enhancedBase64 = await EnhanceAndEncodeImageAsync(imageFile);
        var contentType = imageFile.ContentType;
        var apiKey = _config["OpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new Exception("Missing OpenAI API key in configuration.");

        // Build prompts
        var scorePrompt = BuildScorePromptText();
        var hcpPrompt = BuildHCPExtractionPrompt();
        var parPrompt = BuildParExtractionPrompt();

        // Parallel OpenAI requests
        var scoreTask = SendOpenAIRequest(CreateRequestBody(scorePrompt, enhancedBase64, contentType), apiKey);
        var hcpTask = SendOpenAIRequest(CreateRequestBody(hcpPrompt, enhancedBase64, contentType), apiKey);
        var parTask = SendOpenAIRequest(CreateRequestBody(parPrompt, enhancedBase64, contentType), apiKey);

        await Task.WhenAll(scoreTask, hcpTask, parTask);

        // Parse responses
        var scoreJson = ExtractRawJsonFromResponse(scoreTask.Result);
        var hcpJson = ExtractRawJsonFromResponse(hcpTask.Result);
        var parJson = ExtractRawJsonFromResponse(parTask.Result);

        var scoreData = JsonSerializer.Deserialize<ScoreResponse>(scoreJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var hcpData = JsonSerializer.Deserialize<HcpResponse>(hcpJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var parData = JsonSerializer.Deserialize<ParResponse>(parJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (scoreData == null || hcpData == null || parData == null)
            throw new Exception("Failed to deserialize one or more responses.");

        var holeCount = scoreData.HolesScores.Count;

        var structured = new List<ForeignCourseHole>();
        for (int i = 0; i < holeCount; i++)
        {
            structured.Add(new ForeignCourseHole
            {
                HoleNumber = i + 1,
                Score = scoreData.HolesScores.ElementAtOrDefault(i),
                Hcp = hcpData.HolesHcp.ElementAtOrDefault(i),
                Par = parData.HolesPar.ElementAtOrDefault(i)
            });
        }

        //VerifyParsedHoleData(structured);

        return new ParsedScorecardResult
        {
            ScoreDate = DateTime.Now.ToString("dd.MM.yyyy"),
            ScoreTime = $"{DateTime.Now.Hour:00}:00",
            Holes = structured.Select(h => h.Score).ToList(),
            HoleDetails = structured
        };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ScorecardParserService] Error in structured parallel parsing: {ex.Message}");
        return null;
    }
}

    private string BuildHCPExtractionPrompt()
    {
        return $@"
The image contains 1 **row** of importance: Handicap row. The Par row, Score row and Netto row is not of importance and should not be used: 

1. Identify if the player did 9 or 18 holes. If **IN** is not visible, only 9 holes was played!! If only 1 row of Handicap, Par and Score exists, the player did only 9 holes. Output 9 holes only if so. 
The player can sometimes start an 18 hole round, but only do 9. If so, the second Handicap, Score and Par row will be empty. Give only 9 scores in such cases as well. 

2. Extract HCP from the 'Handicap' row in the provided scorecard image. Create a confidence score on each HCP number and if the confidence score is below 0.93, recheck the HCP number individually. 
Use the knowledge from the score exatraction to know if 9 or 18 holes were played and extract accordingly. 
Do not interpret the Score, Par or Netto rows as HCP. 

Output structure (no comments, ""//"", or anything else than pure json): {{
  ""holesHcp"": []
}}
";
    }
    
    private string BuildParExtractionPrompt()
    {
        return $@"
The image contains 1 **row** of importance: Par row. The Handicap row, Score row and Netto row is not of importance and should not be used. 

1. Identify if the player did 9 or 18 holes. If **IN** is not visible, only 9 holes was played!! If only 1 row of Handicap, Par and Score exists, the player did only 9 holes. Output 9 holes only if so. 
The player can sometimes start an 18 hole round, but only do 9. If so, the second Handicap, Score and Par row will be empty. Give only 9 scores in such cases as well. 

3. Extract Par from the 'Par' row in the provided scorecard image. Create a confidence score on each Par number and if the confidence score is below 0.93, recheck the Par number individually. 
Use the knowledge from the score exatraction to know if 9 or 18 holes were played and extract accordingly. 
Do not interpret the Score, HCP or Netto rows as Par. 

Output structure(no comments, """"//"""", or anything else than pure json): {{
  ""holesPar"": []
}}
";
    }
    
    private string BuildScorePromptText()
    {
        return $@"
The image contains 1 **row** of importance: *Score row*. The Handicap row, Par row and Netto row is not of importance and should not be used. 

Extract golf scores from the 'Score' row in the provided scorecard image. Create a confidence score on each Score number and if the confidence score is below 0.93, recheck the Score number individually.  
Use the knowledge from the first step to know if 9 or 18 holes were played and extract accordingly. 
Do not interpret the Handicap, Par or Netto row as Scores.

Output structure(no comments, """"//"""", or anything else than pure json): {{
  ""holesScores"": []
}}
";
        
    }

    private async Task<string> EnhanceAndEncodeImageAsync(IFormFile imageFile)
    {
        using var inputStream = imageFile.OpenReadStream();
        using var originalImage = await Image.LoadAsync<Rgba32>(inputStream);

        // Enlarge image by 1.5x
        int newWidth = (int)(originalImage.Width * 3);
        int newHeight = (int)(originalImage.Height * 3);

        originalImage.Mutate(ctx =>
        {
            ctx.AutoOrient()
                .Resize(newWidth, newHeight) // 2x upscaling for better OCR
                .Grayscale()
                .Brightness(1.25f)
                .Contrast(1.2f)
                .GaussianSharpen(1.2f)
                .BinaryThreshold(0.80f)
                .GaussianSharpen(3.0f);
        });
        // Save to disk
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "processed_image.png");
        await originalImage.SaveAsPngAsync(filePath);

        // Encode to base64
        using var ms = new MemoryStream();
        await originalImage.SaveAsPngAsync(ms);
        return Convert.ToBase64String(ms.ToArray());
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
        max_tokens = 1500
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
    
    
    public void VerifyParsedHoleData(List<ForeignCourseHole> parsedHoles)
{
    var expected = new List<ForeignCourseHole>
    {
        new() { HoleNumber = 1, Par = 4, Hcp = 15, Score = 4 },
        new() { HoleNumber = 2, Par = 4, Hcp = 1, Score = 6 },
        new() { HoleNumber = 3, Par = 5, Hcp = 7, Score = 6 },
        new() { HoleNumber = 4, Par = 3, Hcp = 13, Score = 5 },
        new() { HoleNumber = 5, Par = 4, Hcp = 11, Score = 5 },
        new() { HoleNumber = 6, Par = 5, Hcp = 3, Score = 6 },
        new() { HoleNumber = 7, Par = 4, Hcp = 17, Score = 5 },
        new() { HoleNumber = 8, Par = 3, Hcp = 9, Score = 3 },
        new() { HoleNumber = 9, Par = 4, Hcp = 5, Score = 4 },
        new() { HoleNumber = 10, Par = 4, Hcp = 2, Score = 4 },
        new() { HoleNumber = 11, Par = 4, Hcp = 4, Score = 5 },
        new() { HoleNumber = 12, Par = 4, Hcp = 18, Score = 5 },
        new() { HoleNumber = 13, Par = 3, Hcp = 8, Score = 4 },
        new() { HoleNumber = 14, Par = 5, Hcp = 10, Score = 8 },
        new() { HoleNumber = 15, Par = 4, Hcp = 12, Score = 4 },
        new() { HoleNumber = 16, Par = 3, Hcp = 14, Score = 3 },
        new() { HoleNumber = 17, Par = 4, Hcp = 16, Score = 4 },
        new() { HoleNumber = 18, Par = 5, Hcp = 6, Score = 5 }
    };

    for (int i = 0; i < expected.Count; i++)
    {
        var expectedHole = expected[i];
        var parsedHole = parsedHoles.ElementAtOrDefault(i);

        if (parsedHole == null)
        {
            Console.WriteLine($"❌ Missing hole at index {i + 1}");
            continue;
        }

        var errors = new List<string>();

        if (parsedHole.HoleNumber != expectedHole.HoleNumber)
            errors.Add($"HoleNumber {parsedHole.HoleNumber} ≠ {expectedHole.HoleNumber}");

        if (parsedHole.Par != expectedHole.Par)
            errors.Add($"Par {parsedHole.Par} ≠ {expectedHole.Par}");

        if (parsedHole.Hcp != expectedHole.Hcp)
            errors.Add($"HCP {parsedHole.Hcp} ≠ {expectedHole.Hcp}");

        if (parsedHole.Score != expectedHole.Score)
            errors.Add($"Score {parsedHole.Score} ≠ {expectedHole.Score}");

        if (errors.Any())
        {
            Console.WriteLine($"❌ Hole {i + 1} mismatch: " + string.Join(", ", errors));
        }
        else
        {
            Console.WriteLine($"✅ Hole {i + 1} OK");
        }
    }

    if (parsedHoles.Count != expected.Count)
        Console.WriteLine($"⚠️ Hole count mismatch: expected {expected.Count}, got {parsedHoles.Count}");
}
}
