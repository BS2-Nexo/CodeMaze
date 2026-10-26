using CodeMaze.MazeGeneration;

namespace CodeMaze.Server.Data.Entities;

public class Score
{
    public int Id { get; set; }

    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    // Maze configuration
    public int MazeSize { get; set; }
    public Difficulty Difficulty { get; set; }

    // Result
    public uint Points { get; set; }
    public double Efficiency { get; set; }
    public bool Perfect { get; set; }

    public string DSLProgram { get; set; } = null!;

    public Guid Session { get; set; } = Guid.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}