using GolfkollektivetBackend.Models;

namespace GolfkollektivetBackend.Services;

public class GolfboxScoreService
{
    private readonly GolfboxAuthService _authService;
    private readonly GolfboxCourseService _courseService;
    private readonly GolfboxMarkerService _markerService;

    public GolfboxScoreService(GolfboxAuthService authService, GolfboxCourseService courseService, GolfboxMarkerService markerService)
    {
        _authService = authService;
        _courseService = courseService;
        _markerService = markerService;
    }

    public async Task<SubmitScoreResult> SubmitScoreAsync(SubmitScoreRequest request)
    {
        var loginResult = await _authService.LoginAndGetHcpAndSelectedGuidAsync(request.Username, request.Password);
        if (string.IsNullOrEmpty(loginResult.SelectedGuid))
            return new SubmitScoreResult { Success = false, ErrorMessage = "Login failed or SelectedGuid missing." };

        var dynamicToken = await _authService.GetDynamicTokenAsync(loginResult.SelectedGuid);
        if (string.IsNullOrEmpty(dynamicToken.PlayerGuid) ||
            string.IsNullOrEmpty(dynamicToken.MagicName) ||
            string.IsNullOrEmpty(dynamicToken.MagicValue))
        {
            return new SubmitScoreResult { Success = false, ErrorMessage = "Dynamic token fields are incomplete." };
        }
        
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

        var success = await _authService.SubmitPreparedScoreFormAsync(loginResult.SelectedGuid, formData);
        if (!success)
            return new SubmitScoreResult { Success = false, ErrorMessage = "Score submission failed." };
        
        await _authService.LogoutAsync();
        
        return new SubmitScoreResult { Success = true, Hcp = loginResult.Hcp };
    }
    
    public async Task<SubmitScoreResult> SubmitForeignScoreAsync(SubmitForeignScoreRequest request)
{
    var loginResult = await _authService.LoginAndGetHcpAndSelectedGuidAsync(request.Username, request.Password);
    if (string.IsNullOrEmpty(loginResult.SelectedGuid))
        return new SubmitScoreResult { Success = false, ErrorMessage = "Login failed or SelectedGuid missing." };

    var dynamicToken = await _authService.GetDynamicTokenAsync(loginResult.SelectedGuid);
    if (string.IsNullOrEmpty(dynamicToken.PlayerGuid) ||
        string.IsNullOrEmpty(dynamicToken.MagicName) ||
        string.IsNullOrEmpty(dynamicToken.MagicValue))
    {
        return new SubmitScoreResult { Success = false, ErrorMessage = "Dynamic token fields are incomplete." };
    }

    var markerGuid = await _markerService.GetMarkerGuidAsync(request.MarkerName);
    if (string.IsNullOrEmpty(markerGuid))
        return new SubmitScoreResult { Success = false, ErrorMessage = "Marker not found." };

    var formData = new Dictionary<string, string>
    {
        ["selected"] = loginResult.SelectedGuid,
        ["command"] = "save",
        [dynamicToken.MagicName] = dynamicToken.MagicValue,
        ["rUrl"] = "/site/my_golfbox/score/whs/newWHSScore.asp",
        ["isHcpQualifying"] = "1",
        ["fld_PlayerMemberGUID"] = dynamicToken.PlayerGuid,
        ["chk_IsCounting"] = "on",
        ["fld_MemberGUID"] = dynamicToken.PlayerGuid,
        ["fld_ScoreDate"] = request.ScoreDate,
        ["fld_ScoreTime"] = request.ScoreTime,
        ["rdo_RoundType"] = "2",
        ["fld_HolesPlayed"] = request.Holes.Count.ToString(),
        ["chk_UnknownCourse"] = "on",
        ["fld_ManualCountryName"] = request.Country,
        ["fld_Club"] = "5c6bdc3c-3d0a-43d0-b4a7-dcc2e9f8b454",
        ["fld_PCC"] = "0",
        ["fld_Course"] = "2FAD3285-6F1A-4B6B-A0BD-904A5E077542",
        ["fld_ManualCourseName"] = request.ManualCourseName,
        ["fld_Tee"] = "5E9F9072-5ABB-43C7-A80B-95CAAE68933A",
        ["fld_ManualTee"] = request.ManualTeeName,
        ["fld_CoursePar"] = request.Par.ToString(),
        ["fld_CourseRating"] = request.CourseRating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
        ["fld_Slope"] = request.Slope.ToString(),
        ["fld_MarkerMemberGUID"] = markerGuid,
        ["chk_InputHoleScores"] = "on",
    };

    foreach (var hole in request.Holes)
    {
        formData[$"Par-{hole.HoleNumber}"] = hole.Par.ToString();
        formData[$"HCP-{hole.HoleNumber}"] = hole.Hcp.ToString();
        formData[$"Strokes-{hole.HoleNumber}"] = hole.Strokes.ToString();
    }

    var success = await _authService.SubmitPreparedScoreFormAsync(loginResult.SelectedGuid, formData);
    if (!success)
        return new SubmitScoreResult { Success = false, ErrorMessage = "Score submission failed." };

    await _authService.LogoutAsync();

    return new SubmitScoreResult { Success = true, Hcp = loginResult.Hcp };
}
}