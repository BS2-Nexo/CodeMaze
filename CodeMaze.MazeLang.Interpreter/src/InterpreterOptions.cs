namespace CodeMaze.MazeLang.Interpreter;

public sealed record InterpreterOptions
{
    /// <summary>Behavior when a move hits a wall or "back" has no history.</summary>
    public CollisionBehavior OnCollision { get; init; } = CollisionBehavior.Crash;
    /// <summary>Maximum number of executed IL instructions (guards against endless loops).</summary>
    public long MaxInstructions { get; init; } = 1_000_000;
    /// <summary>Maximum nesting of function calls (the entry function not counted).</summary>
    public int MaxCallDepth { get; init; } = 100;
    /// <summary>End the run as soon as the exit is reached. If false, ExitReached is emitted once and execution continues.</summary>
    public bool StopAtExit { get; init; } = true;
}