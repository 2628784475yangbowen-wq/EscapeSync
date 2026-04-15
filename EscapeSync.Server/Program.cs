using EscapeSync.Server.Data;
using EscapeSync.Server.Game;
using EscapeSync.Server.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------

// EF Core + SQLite. Connection string in appsettings.json falls back to a local file.
var connection = builder.Configuration.GetConnectionString("Default")
                 ?? "Data Source=escapesync.db";
builder.Services.AddDbContext<GameDbContext>(opt => opt.UseSqlite(connection));
builder.Services.AddScoped<IGameRecordRepository, GameRecordRepository>();

// Game singletons. GameManager holds live in-memory rooms; ticker advances them.
builder.Services.AddSingleton<GameManager>();
builder.Services.AddHostedService<GameTickerService>();

// SignalR with a slightly larger message size ceiling for chat + state snapshots.
builder.Services.AddSignalR(opt => opt.MaximumReceiveMessageSize = 64 * 1024);

// CORS — Blazor WASM client runs on a different dev-time origin.
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(
        "http://localhost:5132",
        "https://localhost:7139")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

builder.Services.AddControllers();

var app = builder.Build();

// ---------------------------------------------------------------------
// Database init — EnsureCreated is fine for a demo; use migrations in production.
// ---------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    db.Database.EnsureCreated();
}

// ---------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------
app.UseCors();

// Lightweight health endpoint + recent-records endpoint for the dashboard / demo.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/records", async (IGameRecordRepository repo) =>
    Results.Ok(await repo.GetRecentAsync(20)));

app.MapHub<GameHub>("/gamehub");

app.Run();
