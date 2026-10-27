using CodeMaze.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace CodeMaze.Server.Controllers;

[ApiController]
[Route("api/players")]
public class PlayersController(PlayerAuthService auth, PlayerSessionStore sessions) : ControllerBase
{
    /// <summary>Reserve a name. Returns a session key right away. 409 = name is taken, so ask the player for their code instead.</summary>
    [HttpPost("reserve")]
    public async Task<IActionResult> Reserve(ReserveRequest req, CancellationToken ct)
    {
        var r = await auth.ReserveAsync(req.DisplayName, ct);
        return r.Result switch
        {
            ReserveResult.InvalidName => BadRequest(new { error = "invalid_name" }),
            ReserveResult.Taken => Conflict(new { error = "name_taken" }),
            _ => Ok(new ReserveResponse(
                r.Player!.Id,
                $"#{r.Player.Id:000}",
                r.Player.DisplayName,
                r.Code!,
                sessions.Create(r.Player)))
        };
    }

    /// <summary>Player retypes the code they were shown to finish registration.</summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(CodeRequest req, CancellationToken ct) =>
        await auth.ConfirmAsync(req.DisplayName, req.Code, ct)
            ? NoContent()
            : BadRequest(new { error = "confirmation_failed" });

    /// <summary>Returning player proves ownership of a name. Returns a session key.</summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(CodeRequest req, CancellationToken ct)
    {
        var o = await auth.VerifyAsync(req.DisplayName, req.Code, ct);
        if (o.Result != VerifyResult.Ok)
            return ToFailure(this, o);

        return Ok(new
        {
            sessionId = sessions.Create(o.Player!),
            playerId = o.Player!.Id,
            publicId = $"#{o.Player.Id:000}",
            displayName = o.Player.DisplayName
        });
    }

    internal static IActionResult ToFailure(ControllerBase c, VerifyOutcome o) => o.Result switch
    {
        VerifyResult.NotFound => c.NotFound(new { error = "unknown_player" }),
        VerifyResult.Locked => c.StatusCode(StatusCodes.Status429TooManyRequests,
            new { error = "locked", lockedUntil = o.LockedUntil }),
        _ => c.Unauthorized(new { error = "wrong_code", lockedUntil = o.LockedUntil })
    };
}