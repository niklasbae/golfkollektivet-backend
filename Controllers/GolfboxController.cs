using GolfkollektivetBackend.Models;
using GolfkollektivetBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace GolfkollektivetBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GolfboxController : ControllerBase
{
    private readonly GolfboxService _golfboxService;
    private readonly GolfboxDataCache _cache;

    public GolfboxController(GolfboxService golfboxService, GolfboxDataCache cache)
    {
        _golfboxService = golfboxService;
        _cache = cache;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitScore([FromBody] SubmitScoreRequest request)
    {
        if (request.HoleScores.Count != 18)
            return BadRequest("Exactly 18 hole scores required.");

        var (hcp, selectedGuid) = await _golfboxService.LoginAndGetHcpAndSelectedGuidAsync(request.Username, request.Password);
        if (string.IsNullOrWhiteSpace(hcp) || string.IsNullOrWhiteSpace(selectedGuid))
            return Unauthorized("Login failed or SelectedGuid not found.");

        var markerResults = await _golfboxService.SearchMarkerAsync(request.MarkerName);
        if (markerResults.Count == 0)
            return BadRequest($"Marker '{request.MarkerName}' not found in GolfBox.");
        var markerGuid = markerResults[0].Guid;

        var (playerGuid, magicName, magicValue, clubs) = await _golfboxService.GetDynamicTokenAsync(selectedGuid);

        var matchingClub = clubs.FirstOrDefault(c => c.Name.Equals(request.ClubName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(matchingClub.Guid))
            return BadRequest($"Club '{request.ClubName}' not found in available options.");
        var clubId = matchingClub.Guid;

        try
        {
            var courseGuid = await _golfboxService.ResolveCourseGuidAsync(clubId, request.CourseName, request.ScoreDate, request.ScoreTime);
            var teeGuid = await _golfboxService.ResolveTeeGuidAsync(courseGuid, request.TeeName, request.TeeGender);

            var scoreSubmission = new SubmitScoreRequest
            {
                Username = request.Username,
                Password = request.Password,
                PlayerGuid = playerGuid,
                MarkerGuid = markerGuid,
                ClubId = clubId,
                CourseGuid = courseGuid,
                TeeGuid = teeGuid,
                SelectedGuid = selectedGuid,
                ScoreDate = request.ScoreDate,
                ScoreTime = request.ScoreTime,
                HoleScores = request.HoleScores
            };

            var success = await _golfboxService.SubmitScoreAsync(scoreSubmission, magicName, magicValue);
            await _golfboxService.LogoutAsync();

            return success
                ? Ok(new SubmitScoreResult { Success = true, Hcp = hcp })
                : StatusCode(500, new SubmitScoreResult { Success = false, ErrorMessage = "Score submission failed." });
        }
        catch (Exception ex)
        {
            return BadRequest($"Could not resolve course or tee: {ex.Message}");
        }
    }

    [HttpGet("search-marker")]
    public async Task<IActionResult> SearchMarker([FromQuery] string name)
    {
        var results = await _golfboxService.SearchMarkerAsync(name);
        return Ok(results);
    }

    [HttpPost("resolve-course-tee")]
    public async Task<IActionResult> ResolveCourseAndTee([FromBody] ResolveCourseTeeRequest request)
    {
        try
        {
            var courseGuid = await _golfboxService.ResolveCourseGuidAsync(
                request.ClubGuid,
                request.CourseName,
                request.ScoreDate,
                request.ScoreTime);

            var teeGuid = await _golfboxService.ResolveTeeGuidAsync(
                courseGuid,
                request.TeeName,
                request.TeeGender);

            return Ok(new
            {
                CourseGuid = courseGuid,
                TeeGuid = teeGuid
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("courses-and-tees")]
    public async Task<IActionResult> GetCoursesAndTeesForClub([FromQuery] string clubGuid)
    {
        try
        {
            var result = await _golfboxService.FetchClubCoursesAndTeesAsync(clubGuid);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("download-cache")]
    public IActionResult DownloadCache()
    {
        var json = _cache.ExportAsJson();
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", "golfbox-cache.json");
    }
    
    [HttpPost("fetch-all-club-data")]
    public async Task<IActionResult> FetchAllClubData([FromBody] FetchClubDataRequest request, [FromServices] FetchAllClubData fetcher)
    {
        if (request.Clubs == null || request.Clubs.Count == 0)
            return BadRequest("Provide at least one club.");

        var input = request.Clubs.Select(c => (c.Name, c.Guid)).ToList();
        var result = await fetcher.FetchAndCacheAllClubsAsync(input);

        return Ok(result);
    }
}
