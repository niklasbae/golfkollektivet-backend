using GolfkollektivetBackend.Models;

namespace GolfkollektivetBackend.Services;

public class GolfboxScoreService
{
    private readonly GolfboxAuthService _authService;
    private readonly GolfboxCourseService _courseService;
    private readonly GolfboxMarkerService _markerService;
    private readonly HttpClient _httpClient;

    public GolfboxScoreService(GolfboxAuthService authService, GolfboxCourseService courseService, GolfboxMarkerService markerService, IHttpClientFactory clientFactory)
    {
        _authService = authService;
        _courseService = courseService;
        _markerService = markerService;
        _httpClient = clientFactory.CreateClient("Golfbox");
    }

    public async Task<SubmitScoreResult> SubmitScoreAsync(SubmitScoreRequest request)
    {
        var loginResult = await _authService.LoginAndGetHcpAndSelectedGuidAsync(request.Username, request.Password);
        if (string.IsNullOrEmpty(loginResult.SelectedGuid))
            return new SubmitScoreResult { Success = false, ErrorMessage = "Login failed or SelectedGuid missing." };

        var dynamicToken = await _authService.GetDynamicTokenAsync(loginResult.SelectedGuid);

        var markerGuid = await _markerService.GetMarkerGuidAsync(request.MarkerName);
        if (string.IsNullOrEmpty(markerGuid))
            return new SubmitScoreResult { Success = false, ErrorMessage = "Marker not found." };

        var clubId = dynamicToken.Clubs.FirstOrDefault(c => c.Name.Equals(request.ClubName, StringComparison.OrdinalIgnoreCase)).Guid;
        if (string.IsNullOrEmpty(clubId))
            return new SubmitScoreResult { Success = false, ErrorMessage = "Club not found." };

        var resolveResult = await _courseService.ResolveCourseAndTeeAsync(new ResolveCourseTeeRequest
        {
            ClubGuid = clubId,
            CourseName = request.CourseName,
            TeeName = request.TeeName,
            TeeGender = request.TeeGender,
            ScoreDate = request.ScoreDate,
            ScoreTime = request.ScoreTime
        });

        if (!resolveResult.Success)
            return new SubmitScoreResult { Success = false, ErrorMessage = resolveResult.ErrorMessage };

        var (par, rating, slope, pcc, isHcpQualifying) = await _courseService.FetchCourseStatsAsync(
            resolveResult.CourseGuid, resolveResult.TeeGuid, dynamicToken.PlayerGuid, request.ScoreDate);

        var formData = new Dictionary<string, string>
        {
            ["selected"] = loginResult.SelectedGuid,
            ["command"] = "save",
            [dynamicToken.MagicName] = dynamicToken.MagicValue,
            ["rUrl"] = "/site/my_golfbox/score/whs/newWHSScore.asp",
            ["isHcpQualifying"] = isHcpQualifying,
            ["fld_PlayerMemberGUID"] = dynamicToken.PlayerGuid,
            ["chk_IsCounting"] = "on",
            ["fld_MemberGUID"] = dynamicToken.PlayerGuid,
            ["fld_ScoreDate"] = request.ScoreDate,
            ["fld_ScoreTime"] = request.ScoreTime,
            ["rdo_RoundType"] = "2",
            ["fld_HolesPlayed"] = request.HoleScores.Count.ToString(),
            ["fld_Club"] = clubId,
            ["fld_PCC"] = pcc,
            ["fld_Course"] = resolveResult.CourseGuid,
            ["fld_Tee"] = resolveResult.TeeGuid,
            ["fld_CoursePar"] = par,
            ["fld_CourseRating"] = rating,
            ["fld_Slope"] = slope,
            ["fld_MarkerMemberGUID"] = markerGuid,
            ["chk_InputHoleScores"] = "on"
        };

        for (int i = 0; i < request.HoleScores.Count; i++)
            formData[$"ScoreHole_{i}"] = request.HoleScores[i].ToString();

        var response = await _httpClient.PostAsync(
            $"https://www.golfbox.no/site/my_golfbox/score/whs/newWHSScore.asp?selected={loginResult.SelectedGuid}",
            new FormUrlEncodedContent(formData));

        var body = await response.Content.ReadAsStringAsync();

        var success = body.Contains("Score er lagret") ||
                      response.Headers.Location?.ToString().Contains("listScoresToConfirm.asp") == true;

        await _authService.LogoutAsync();

        return success
            ? new SubmitScoreResult { Success = true, Hcp = loginResult.Hcp }
            : new SubmitScoreResult { Success = false, ErrorMessage = "Score submission failed." };
    }
}