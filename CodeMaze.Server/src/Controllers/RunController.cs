using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;
using CodeMaze.MazeLang.Compiler;
using CodeMaze.MazeLang.Parser.Text;
using CodeMaze.Server.Runs;
using Microsoft.AspNetCore.Mvc;

namespace CodeMaze.Server.Controllers;

/// <summary>
/// Language: give either <c>Language</c> (the name of a language the server has, see
/// /api/capabilities) or <c>LanguageDefinition</c> (the text of a custom language definition, TOML).
/// If both are given the definition wins.
/// Maze is optional. If it is missing, Size (and optionally Difficulty) are used to generate one.
/// StepDelayMs paces the event stream so the frontend can animate (0 = as fast as possible).
/// </summary>
public sealed record CreateRunRequest(
    string Code,
    string? Language,
    string? LanguageDefinition,
    Maze? Maze,
    int? Size,
    Difficulty? Difficulty,
    int? StepDelayMs);

public sealed record CreateRunResponse(Guid RunId, string WebSocketPath, Maze Maze);

/// <param name="Source">"language" for problems in the language definition, "code" for problems in the program.</param>
public sealed record RunError(string Source, string Message, int? Line, int? Column);

[ApiController]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly MazeCompiler _compiler;
    private readonly LanguageRegistry _languages;
    private readonly IRunStore _runs;
    private readonly RunSession _session;

    public RunsController(MazeCompiler compiler, LanguageRegistry languages, IRunStore runs, RunSession session)
    {
        _compiler = compiler;
        _languages = languages;
        _runs = runs;
        _session = session;
    }

    /// <summary>
    /// Step 1: the frontend sends the code, the language, and either a finished maze or the
    /// details to generate one. The code is compiled right away, so errors come back here as a
    /// 400 and not over the socket. Returns the maze that will be used and the socket path.
    /// </summary>
    [HttpPost]
    public ActionResult<CreateRunResponse> Create(CreateRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest("code is required.");

        if (request.Code.Length > ApiLimits.MaxCodeLength)
            return BadRequest($"code must not exceed {ApiLimits.MaxCodeLength} characters.");

        // ---- language: custom definition (text) or one of the server's languages (name)
        LanguageDefinition language;

        if (!string.IsNullOrWhiteSpace(request.LanguageDefinition))
        {
            if (request.LanguageDefinition.Length > ApiLimits.MaxLanguageDefinitionLength)
                return BadRequest($"languageDefinition must not exceed {ApiLimits.MaxLanguageDefinitionLength} characters.");

            try
            {
                language = LanguageToml.Parse(request.LanguageDefinition, "languageDefinition");
            }
            catch (LanguageFormatException ex)
            {
                return BadRequest(new { errors = ex.Errors.Select(m => new RunError("language", m, null, null)) });
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.Language))
        {
            if (!_languages.TryGet(request.Language, out var known))
            {
                var available = string.Join(", ", _languages.List().Select(l => l.Name));
                return BadRequest($"Unknown language '{request.Language}'. Available: {available}.");
            }

            language = known;
        }
        else
        {
            return BadRequest("Provide a language name or a languageDefinition.");
        }

        // ---- compile
        if (!_compiler.TryCompile(request.Code, language, out var compiled, out var diagnostics))
        {
            return BadRequest(new
            {
                errors = diagnostics.Select(d =>
                    new RunError("code", d.Message, d.Location?.Line, d.Location?.Column)),
            });
        }

        // ---- maze
        Maze maze;

        if (request.Maze is not null)
        {
            if ((long)request.Maze.Width * request.Maze.Height > ApiLimits.MaxMazeCells)
                return BadRequest("The provided maze is too large.");

            maze = request.Maze;
        }
        else
        {
            if (request.Size is not { } size || size < ApiLimits.MinMazeSize || size > ApiLimits.MaxMazeSize)
                return BadRequest($"Provide a maze, or a size between {ApiLimits.MinMazeSize} and {ApiLimits.MaxMazeSize}.");

            var difficulty = request.Difficulty ?? Difficulty.Medium;
            if (!Enum.IsDefined(difficulty))
                return BadRequest("Unknown difficulty.");


            var draftMaze = new DraftMaze(size, size, Walls.All);

            var rand = new Random();
            var algo = new RecursiveBacktracker(rand);
            algo.Generate(draftMaze);
            maze = draftMaze.Finalize();
        }

        var delayMs = Math.Clamp(request.StepDelayMs ?? ApiLimits.DefaultStepDelayMs, ApiLimits.MinStepDelayMs, ApiLimits.MaxStepDelayMs);
        var run = _runs.Add(compiled.Il, maze, TimeSpan.FromMilliseconds(delayMs));

        return Ok(new CreateRunResponse(run.Id, $"/api/runs/{run.Id}/ws", maze));
    }

    /// <summary>
    /// Step 2: the frontend opens a WebSocket here. The interpreter starts on connect
    /// and its events are streamed until it finishes or the client sends { "type": "cancel" }.
    /// </summary>
    [HttpGet("{id:guid}/ws")]
    public async Task Connect(Guid id, CancellationToken requestAborted)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!_runs.TryTake(id, out var run))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await _session.RunAsync(socket, run, requestAborted);
    }
}