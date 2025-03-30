namespace GolfkollektivetBackend.Models;

public class SubmitScoreResult
{
    public bool Success { get; set; }
    
    /// <summary>
    /// Current Handicap (HCP) of the user after submission, if successful.
    /// </summary>
    public string? Hcp { get; set; }

    /// <summary>
    /// Error message if submission failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}