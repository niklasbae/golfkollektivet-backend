using System.Net;
using GolfkollektivetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

void ConfigureGolfboxHttpClient<T>() where T : class =>
    builder.Services.AddHttpClient<T>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = true,
        CookieContainer = new CookieContainer()
    });

ConfigureGolfboxHttpClient<GolfboxAuthService>();
ConfigureGolfboxHttpClient<GolfboxCourseService>();
ConfigureGolfboxHttpClient<GolfboxMarkerService>();

builder.Services.AddScoped<GolfboxScoreService>();
builder.Services.AddSingleton<GolfboxDataCache>();
builder.Services.AddScoped<GolfboxDataSeeder>();

// CORS (optional for frontend)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// if (app.Environment.IsDevelopment())
// {
    app.UseDeveloperExceptionPage();
// }
// else
// {
//     app.UseExceptionHandler("/error");
// }

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();