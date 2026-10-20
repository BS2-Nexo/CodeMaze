using System.Text.Json;
using CodeMaze.MazeLang.Interpreter;
using CodeMaze.Server.Runs;
using Microsoft.AspNetCore.Mvc;

namespace CodeMaze.Server.Controllers;

public sealed record MazeCapabilities(int MinSize, int MaxSize, long MaxCells);

public sealed record RunCapabilities(
    int MaxCodeLength,
    int MaxLanguageDefinitionLength,
    int MinStepDelayMs,
    int MaxStepDelayMs,
    int DefaultStepDelayMs);

public sealed record InterpreterCapabilities(string OnCollision, bool StopAtExit);

public sealed record CapabilitiesResponse(
    IReadOnlyList<LanguageInfo> Languages,
    IReadOnlyList<string> Difficulties,
    string DefaultDifficulty,
    MazeCapabilities Maze,
    RunCapabilities Run,
    InterpreterCapabilities Interpreter);

[ApiController]
[Route("api/capabilities")]
public class CapabilitiesController : ControllerBase
{
    private readonly LanguageRegistry _languages;
    private readonly InterpreterOptions _interpreterOptions;

    public CapabilitiesController(LanguageRegistry languages, InterpreterOptions interpreterOptions)
    {
        _languages = languages;
        _interpreterOptions = interpreterOptions;
    }

    /// <summary>What this server supports, so the frontend does not have to hard-code it.</summary>
    [HttpGet]
    public ActionResult<CapabilitiesResponse> Get()
    {
        var camelCase = JsonNamingPolicy.CamelCase;

        return Ok(new CapabilitiesResponse(
            _languages.List(),
            Enum.GetNames<Difficulty>().Select(camelCase.ConvertName).ToList(),
            camelCase.ConvertName(nameof(Difficulty.Medium)),
            new MazeCapabilities(ApiLimits.MinMazeSize, ApiLimits.MaxMazeSize, ApiLimits.MaxMazeCells),
            new RunCapabilities(
                ApiLimits.MaxCodeLength,
                ApiLimits.MaxLanguageDefinitionLength,
                ApiLimits.MinStepDelayMs,
                ApiLimits.MaxStepDelayMs,
                ApiLimits.DefaultStepDelayMs),
            new InterpreterCapabilities(
                camelCase.ConvertName(_interpreterOptions.OnCollision.ToString()),
                _interpreterOptions.StopAtExit)));
    }
}