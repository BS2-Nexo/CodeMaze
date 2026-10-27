using CodeMaze.MazeGeneration.Algorithms;
using CodeMaze.Server.Data;
using CodeMaze.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMaze.Server.Services;

public sealed class RunScoringService(
    RunResultQueue queue,
    IDbContextFactory<CodeMazeDbContext> dbFactory,
    PlayerSessionStore sessionStore,
    ILogger<RunScoringService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var done in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ScoreAsync(done, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Run {RunId}: scoring failed.", done.Run.Id);
            }
        }
    }

    private async Task ScoreAsync(CompletedRun done, CancellationToken ct)
    {
        var (run, stats) = (done.Run, done.Stats);

        if (run.Maze.ShortestPath is not { Count: > 0 } path)
        {
            logger.LogError("Run {RunId}: maze has no shortest path, the run is not scored.", run.Id);
            return;
        }

        if (!sessionStore.TryGet(run.SessionId, out var player))
        {
            logger.LogWarning("Run {RunId}: session {SessionId} unknown, score not saved.", run.Id, run.SessionId);
            return;
        }

        var score = MazeScorer.Score(new MazeScoreInput(
            run.Maze.Height,
            run.Maze.Difficulty,
            path.Count - 1,      // optimal moves
            stats.Steps,         // actual moves
            stats.ReachedExit));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Scores.Add(new Score
        {
            PlayerId = player.Id,
            MazeSize = run.Maze.Height,
            Difficulty = run.Maze.Difficulty,
            Points = score.Points,
            Efficiency = score.Efficiency,
            Perfect = score.Perfect,
            Session = run.SessionId,
            DSLProgram = "PLACEHOLDERVALUE",
        });
        await db.SaveChangesAsync(ct);
    }
}