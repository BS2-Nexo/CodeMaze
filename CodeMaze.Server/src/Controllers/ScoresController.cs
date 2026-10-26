using CodeMaze.Server.Data;
using CodeMaze.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CodeMaze.Server.Controllers;

[ApiController]
[Route("api/scores")]
public class ScoresController(CodeMazeDbContext db) : ControllerBase
{
    /// <summary>
    /// Ranks individual runs by total points.
    /// Scores within the same session are summed.
    /// Pass top=0 to return all runs.
    /// </summary>
    [HttpGet("leaderboard")]
    public async Task<IActionResult> Leaderboard(
        [FromQuery] int top = 10,
        CancellationToken ct = default)
    {
        if (top < 0)
        {
            return BadRequest("The top parameter must be 0 or greater.");
        }

        var query = db.Scores
            .Where(s => s.Player.ConfirmedAt != null)
            .GroupBy(s => new { s.Session, s.PlayerId, s.Player.DisplayName })
            .Select(g => new
            {
                g.Key.Session,
                g.Key.PlayerId,
                g.Key.DisplayName,
                TotalPoints = g.Sum(s => (long)s.Points),
                AverageEfficiency = g.Average(s => s.Efficiency),
                LastPlayedAt = g.Max(s => s.CreatedAt)
            })
            .OrderByDescending(x => x.TotalPoints)
            .ThenBy(x => x.LastPlayedAt)
            .ThenBy(x => x.Session);

        var rows = top == 0
            ? await query.ToListAsync(ct)
            : await query.Take(top).ToListAsync(ct);

        return Ok(rows.Select((x, i) => new
        {
            Rank = i + 1,
            x.Session,
            PlayerTag = $"#{x.PlayerId:000}",
            x.DisplayName,
            x.TotalPoints,
            x.AverageEfficiency,
            x.LastPlayedAt
        }));
    }
}