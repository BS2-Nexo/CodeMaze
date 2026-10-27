using System.Collections.Concurrent;
using CodeMaze.MazeGeneration;
using CodeMaze.MazeLang.IL;

namespace CodeMaze.Server.Runs;

/// <summary>A compiled program plus the maze to run it in, waiting for the WebSocket to connect.</summary>
public sealed record PendingRun(
    Guid Id,
    ILProgram Program,
    Maze Maze,
    TimeSpan StepDelay,
    DateTimeOffset CreatedAt,
    Guid SessionId);

public interface IRunStore
{
    PendingRun Add(ILProgram program, Maze maze, TimeSpan stepDelay, Guid SessionId);

    /// <summary>Removes and returns the run, so a run can only be started once.</summary>
    bool TryTake(Guid id, out PendingRun run);
}

/// <summary>
/// Holds runs between the POST (prepare) and the WebSocket connect (execute).
/// In-memory, so it only works with a single server instance.
/// </summary>
public sealed class InMemoryRunStore : IRunStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, PendingRun> _runs = new();

    public PendingRun Add(ILProgram program, Maze maze, TimeSpan stepDelay, Guid SessionId)
    {
        Purge();

        var run = new PendingRun(Guid.NewGuid(), program, maze, stepDelay, DateTimeOffset.UtcNow, SessionId);
        _runs[run.Id] = run;
        return run;
    }

    public bool TryTake(Guid id, out PendingRun run)
    {
        if (_runs.TryRemove(id, out var found) && DateTimeOffset.UtcNow - found.CreatedAt <= MaxAge)
        {
            run = found;
            return true;
        }

        run = null!;
        return false;
    }

    // Drops runs that were prepared but never connected to.
    private void Purge()
    {
        var cutoff = DateTimeOffset.UtcNow - MaxAge;
        foreach (var (id, run) in _runs)
        {
            if (run.CreatedAt < cutoff)
                _runs.TryRemove(id, out _);
        }
    }
}