using GolfkollektivetBackend.Models;
using GolfkollektivetBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace GolfkollektivetBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GolfboxController : ControllerBase
{
    private readonly GolfboxService _golfboxService;
    private readonly GolfboxGuidMapService _guidMapService;

    public GolfboxController(GolfboxService golfboxService, GolfboxGuidMapService guidMapService)
    {
        _golfboxService = golfboxService;
        _guidMapService = guidMapService;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitScore([FromBody] SubmitScoreRequest request)
    {
        if (request.HoleScores.Count != 18)
            return BadRequest("Exactly 18 hole scores required.");

        var loginResult = await _golfboxService.LoginAndGetHcpAndSelectedGuidAsync(request.Username, request.Password);
        if (string.IsNullOrWhiteSpace(loginResult.Hcp) || string.IsNullOrWhiteSpace(loginResult.SelectedGuid))
            return Unauthorized("Login failed or SelectedGuid not found.");

        var hcp = loginResult.Hcp;
        var selectedGuid = loginResult.SelectedGuid;

        var markerResults = await _golfboxService.SearchMarkerAsync(request.MarkerName);
        if (markerResults.Count == 0)
            return BadRequest($"Marker '{request.MarkerName}' not found in GolfBox.");

        var markerGuid = markerResults[0].Guid;

        string clubId, courseGuid, teeGuid;
        try
        {
            clubId = _guidMapService.GetClubId(request.ClubName);
            courseGuid = _guidMapService.GetCourseGuid(request.ClubName, request.CourseName);
            teeGuid = _guidMapService.GetTeeGuid(request.ClubName, request.CourseName, request.TeeName);
        }
        catch (Exception ex)
        {
            return BadRequest($"Mapping error: {ex.Message}");
        }

        var (playerGuid, magicName, magicValue) = await _golfboxService.GetDynamicTokenAsync(selectedGuid);

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

    [HttpGet("search-marker")]
    public async Task<IActionResult> SearchMarker([FromQuery] string name)
    {
        var results = await _golfboxService.SearchMarkerAsync(name);
        return Ok(results);
    }
}