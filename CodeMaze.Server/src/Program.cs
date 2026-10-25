using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Generator;
using CodeMaze.MazeLang.Compiler;
using CodeMaze.MazeLang.Interpreter;
using CodeMaze.Server.Runs;

var builder = WebApplication.CreateBuilder(args);

// Origins of the frontend, e.g. appsettings.Development.json:
//   "Cors": { "AllowedOrigins": [ "http://localhost:5173" ] }
// Not needed when the frontend is served from the same origin as this API.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

// Folder with one TOML file per built-in language (relative to the content root).
//   "Languages": { "Directory": "Languages" }
var languageDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["Languages:Directory"] ?? "Languages");

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        // Lets the frontend send "difficulty": "hard" instead of a number.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// --- maze generation ---------------------------------------------------------
//builder.Services.AddSingleton<IMazeGenerator, MazeGenerator>();            // remove if nothing uses it anymore
builder.Services.AddMazeGeneration(builder.Configuration);

// --- language pipeline -------------------------------------------------------
builder.Services.AddSingleton(LanguageRegistry.LoadFromDirectory(languageDirectory));   // fails fast on a bad file
builder.Services.AddSingleton<MazeCompiler>();
builder.Services.AddSingleton(new InterpreterOptions());                    // MaxInstructions, MaxCallDepth, OnCollision, StopAtExit

// --- runs ----------------------------------------------------------------------
builder.Services.AddSingleton<IRunStore, InMemoryRunStore>();
builder.Services.AddScoped<RunSession>();

var app = builder.Build();

app.UseCors();

var webSocketOptions = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) };
foreach (var origin in allowedOrigins)
    webSocketOptions.AllowedOrigins.Add(origin);   // empty list = any origin may connect
app.UseWebSockets(webSocketOptions);               // must come before MapControllers

app.MapControllers();

app.Run();