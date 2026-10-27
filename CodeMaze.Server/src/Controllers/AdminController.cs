using System.Security.Cryptography;
using System.Text;
using CodeMaze.Server.Data;
using CodeMaze.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CodeMaze.Server.Controllers;

/// <summary>Prize-desk tools. Protected by an X-Admin-Key header (config: Arcade:AdminKey).</summary>
[ApiController]
[Route("api/admin")]
public class AdminController(CodeMazeDbContext db, PlayerAuthService auth, IConfiguration config) : ControllerBase
{
    private bool IsAdmin()
    {
        var expected = config["CodeMaze:AdminKey"];
        if (string.IsNullOrEmpty(expected)) return false;
        var given = Request.Headers["X-Admin-Key"].ToString();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }

    [HttpPost("players/{id:int}/reissue")]
    public async Task<IActionResult> Reissue(int id, CancellationToken ct)
    {
        if (!IsAdmin()) return Unauthorized();
        var code = await auth.ReissueAsync(id, ct);
        return code is null ? NotFound() : Ok(new { code });
    }

    /// <summary>Stored solutions for the "explain your program" fallback.</summary>
    [HttpGet("players/{id:int}/scores")]
    public async Task<IActionResult> PlayerScores(int id, CancellationToken ct)
    {
        if (!IsAdmin()) return Unauthorized();
        var scores = await db.Scores.Where(s => s.PlayerId == id)
            .OrderByDescending(s => s.Points).ThenBy(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Points, s.DSLProgram, s.CreatedAt })
            .ToListAsync(ct);
        return Ok(scores);
    }
}