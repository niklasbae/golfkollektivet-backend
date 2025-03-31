using System.Net;
using GolfkollektivetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// HTTP Client registration for multiple services
builder.Services.AddHttpClient<GolfboxAuthService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    });

builder.Services.AddHttpClient<GolfboxCourseService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    });

builder.Services.AddHttpClient<GolfboxMarkerService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    });

// Other services
builder.Services.AddScoped<GolfboxScoreService>();
builder.Services.AddSingleton<GolfboxDataCache>();
builder.Services.AddScoped<GolfboxDataSeeder>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();
app.MapControllers();

app.Run();