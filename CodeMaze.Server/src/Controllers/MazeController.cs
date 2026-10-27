using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace CodeMaze.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MazeController : ControllerBase
{
    private const int MinSize = 2;
    private const int MaxSize = 120;

    private readonly IMazeGenerator _generator;

    public MazeController(IMazeGenerator generator)
    {
        _generator = generator;
    }

    /// <summary>
    /// GET api/maze?size=10
    /// Generates a square maze using any configured algorithm for that size, regardless of difficulty.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Maze), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Maze> Generate([FromQuery] int size)
    {
        if (!IsValidSize(size))
            return BadRequest($"size must be between {MinSize} and {MaxSize}.");

        try
        {
            var draft = _generator.Generate(size, size);
            return Ok(draft.Finalize());
        }
        catch (NoGeneratorFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// GET api/maze/medium?size=10
    /// Generates a square maze of size x size cells for the given difficulty.
    /// </summary>
    [HttpGet("{difficulty}")]
    [ProducesResponseType(typeof(Maze), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Maze> Generate(Difficulty difficulty, [FromQuery] int size)
    {
        if (!IsValidSize(size))
            return BadRequest($"size must be between {MinSize} and {MaxSize}.");

        if (!Enum.IsDefined(difficulty))
            return BadRequest("Unknown difficulty.");

        try
        {
            var draft = _generator.Generate(size, size, difficulty);
            return Ok(draft.Finalize());
        }
        catch (NoGeneratorFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private static bool IsValidSize(int size) => size is >= MinSize and <= MaxSize;
}