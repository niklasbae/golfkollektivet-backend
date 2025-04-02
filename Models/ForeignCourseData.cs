namespace GolfkollektivetBackend.Models;

public class ForeignCourseData
{
    public int CoursePar { get; set; }
    public double CourseRating { get; set; }
    public int Slope { get; set; }
    public string ManualTee { get; set; } = string.Empty;
    public string ManualCourseName { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Note { get; set; }  
    public List<ForeignCourseHole> Holes { get; set; } = new();
}

public class ForeignCourseHole
{
    public int HoleNumber { get; set; }
    public int Par { get; set; }
    public int Hcp { get; set; }
    public int Score { get; set; }
}