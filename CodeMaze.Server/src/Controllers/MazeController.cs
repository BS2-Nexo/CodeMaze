using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;
using Microsoft.AspNetCore.Mvc;

namespace CodeMaze.Server.Controllers;

public enum Difficulty
{
    Easy,
    Medium,
    Hard
}

[ApiController]
[Route("api/[controller]")]
public class MazeController : ControllerBase
{
    private const int MinSize = 2;
    private const int MaxSize = 100;


    /// <summary>
    /// GET api/maze?size=10&amp;difficulty=medium
    /// Generates a square maze of size x size cells.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Maze), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<Maze> Generate([FromQuery] int size, [FromQuery] Difficulty difficulty = Difficulty.Medium)
    {
        if (size < MinSize || size > MaxSize)
        {
            return BadRequest($"size must be between {MinSize} and {MaxSize}.");
        }

        if (!Enum.IsDefined(difficulty))
        {
            return BadRequest("Unknown difficulty.");
        }

        var maze = new DraftMaze(size, size, Walls.All);

        var rand = new Random();
        var algo = new RecursiveBacktracker(rand);
        algo.Generate(maze);
        var final = maze.Finalize();

        return Ok(final);
    }
}