using GolfkollektivetBackend.Models;
using GolfkollektivetBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace GolfkollektivetBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GolfboxController : ControllerBase
{
    private readonly GolfboxScoreService _scoreService;
    private readonly GolfboxMarkerService _markerService;
    private readonly GolfboxCourseService _courseService;
    private readonly GolfboxDataCache _cache;
    private readonly GolfboxDataSeeder _dataSeeder;

    public GolfboxController(
        GolfboxScoreService scoreService,
        GolfboxMarkerService markerService,
        GolfboxCourseService courseService,
        GolfboxDataCache cache,
        GolfboxDataSeeder dataSeeder)
    {
        _scoreService = scoreService;
        _markerService = markerService;
        _courseService = courseService;
        _cache = cache;
        _dataSeeder = dataSeeder;
    }

    [HttpPost("submit-score")]
    public async Task<IActionResult> SubmitScore([FromBody] SubmitScoreRequest request)
    {
        var result = await _scoreService.SubmitScoreAsync(request);
        return result.Success ? Ok(result) : StatusCode(500, result.ErrorMessage);
    }

    [HttpGet("search-marker")]
    public async Task<IActionResult> SearchMarker([FromQuery] string name)
    {
        var results = await _markerService.SearchAsync(name);
        return Ok(results);
    }

    [HttpPost("resolve-course-tee")]
    public async Task<IActionResult> ResolveCourseAndTee([FromBody] ResolveCourseTeeRequest request)
    {
        var result = await _courseService.ResolveCourseAndTeeAsync(request);
        return result.Success ? Ok(result) : BadRequest(result.ErrorMessage);
    }

    [HttpGet("courses-and-tees")]
    public async Task<IActionResult> GetCoursesAndTees([FromQuery] string clubGuid)
    {
        var result = await _courseService.FetchClubCoursesAndTeesAsync(clubGuid); // Corrected method name
        return Ok(result);
    }

    [HttpGet("download-cache")]
    public IActionResult DownloadCache()
    {
        var json = _cache.ExportAsJson();
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", "golfbox-cache.json");
    }

    [HttpPost("fetch-all-club-data")]
    public async Task<IActionResult> FetchAllClubData([FromBody] FetchClubDataRequest request)
    {
        var result = await _dataSeeder.FetchAndCacheAllClubsAsync(request.Clubs);
        return Ok(result);
    }
}