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
    private readonly ScorecardParserService _parserService;

    
    public GolfboxController(
        GolfboxScoreService scoreService,
        GolfboxMarkerService markerService,
        GolfboxCourseService courseService,
        GolfboxDataCache cache,
        GolfboxDataSeeder dataSeeder,
        ScorecardParserService parserService)
    {
        _scoreService = scoreService;
        _markerService = markerService;
        _courseService = courseService;
        _cache = cache;
        _dataSeeder = dataSeeder;
        _parserService = parserService;
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
    
    [HttpPost("/api/scorecard/parse")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ParseScorecard([FromForm] ScorecardUploadRequest request)
    {
        if (request.Image == null || request.Image.Length == 0)
            return BadRequest("No image uploaded");

        var result = await _parserService.ParseScorecardAsync(request.Image);

        if (result == null)
            return StatusCode(500, "Failed to parse image");

        // ✅ Return only minimal shaped response
        return Ok(new
        {
            scoreDate = result.ScoreDate,
            scoreTime = result.ScoreTime,
            holes = result.Holes
        });
    }
    
    [HttpGet("clubs")]
    public IActionResult GetAllClubs()
    {
        var clubs = _cache.Load()
            .Select(c => new
            {
                clubGuid = c.ClubGuid,
                clubName = c.ClubName,
                courseCount = c.Courses.Count
            })
            .OrderBy(c => c.clubName)
            .ToList();

        return Ok(clubs);
    }

    [HttpGet("clubs/{clubGuid}/courses")]
    public IActionResult GetCoursesForClub(string clubGuid)
    {
        var club = _cache.Load().FirstOrDefault(c => c.ClubGuid == clubGuid);
        if (club == null)
            return NotFound("Club not found");

        return Ok(club.Courses.Select(c => new
        {
            courseGuid = c.CourseGuid,
            courseName = c.CourseName,
            tees = c.Tees.Select(t => new
            {
                teeGuid = t.TeeGuid,
                teeName = t.TeeName,
                teeGender = t.TeeGender
            })
        }));
    }
}