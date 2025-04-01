using Microsoft.AspNetCore.Http;

namespace GolfkollektivetBackend.Models;

public class ScorecardUploadRequest
{
    public IFormFile Image { get; set; } = default!;
}