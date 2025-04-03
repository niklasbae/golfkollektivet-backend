namespace GolfkollektivetBackend.Models;

public class SubmitForeignScoreRequest
{
    public string Username { get; set; }
    public string Password { get; set; }

    public string Country { get; set; }
    public string ManualCourseName { get; set; }
    public string ManualTeeName { get; set; }
    public string ScoreDate { get; set; }
    public string ScoreTime { get; set; }

    public string MarkerGuid { get; set; }

    public int Par { get; set; }
    public double CourseRating { get; set; }
    public int Slope { get; set; }

    public List<HoleDetail> Holes { get; set; }

    public class HoleDetail
    {
        public int HoleNumber { get; set; }
        public int Par { get; set; }
        public int Hcp { get; set; }
        public int Strokes { get; set; }
    }
}