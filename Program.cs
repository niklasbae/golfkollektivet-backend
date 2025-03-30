// Program.cs
using System.Net;
using GolfkollektivetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register GolfboxGuidMapService (required by GolfboxController)
builder.Services.AddSingleton<GolfboxGuidMapService>();

// Register GolfboxService with HTTP client support and cookies
builder.Services.AddHttpClient<GolfboxService>(nameof(GolfboxService))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    });

var app = builder.Build();

// Middleware setup
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();
app.MapControllers();

app.Run();