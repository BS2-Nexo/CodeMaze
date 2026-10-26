using System.Text.RegularExpressions;
using CodeMaze.Server.Data;
using CodeMaze.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMaze.Server.Services;

public enum VerifyResult { Ok, NotFound, WrongCode, Locked }
public record VerifyOutcome(VerifyResult Result, Player? Player = null, DateTimeOffset? LockedUntil = null);

public enum ReserveResult { Created, Taken, InvalidName }
public record ReserveOutcome(ReserveResult Result, Player? Player = null, string? Code = null);

public static class NameRules
{
    private static readonly Regex Allowed = new(@"^[A-Z0-9 _.\-!]{2,16}$", RegexOptions.Compiled);

    // TODO: fill with your own blocklist. Checked as substring on the normalized name.
    private static readonly string[] Blocked = [];

    public static string Normalize(string name) =>
        Regex.Replace(name.Trim().ToUpperInvariant(), @"\s+", " ");

    public static bool IsValid(string normalized) =>
        Allowed.IsMatch(normalized) && !Blocked.Any(normalized.Contains);
}

public class PlayerAuthService(
    CodeMazeDbContext db,
    IPickupCodeGenerator generator,
    CodeHasher hasher,
    TimeProvider time)
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(10);

    /// <summary>Step 1: reserve a name and hand out a code. Player is pending until confirmed.</summary>
    public async Task<ReserveOutcome> ReserveAsync(string displayName, CancellationToken ct = default)
    {
        var normalized = NameRules.Normalize(displayName);
        if (!NameRules.IsValid(normalized)) return new(ReserveResult.InvalidName);

        var now = time.GetUtcNow();
        var existing = await db.Players.FirstOrDefaultAsync(p => p.NameNormalized == normalized, ct);

        if (existing is not null)
        {
            var expired = existing.ConfirmedAt is null && existing.ReservedUntil < now;
            if (!expired) return new(ReserveResult.Taken);

            db.Players.Remove(existing);
            await db.SaveChangesAsync(ct); // separate save so the unique index is free before the insert
        }

        var code = generator.Generate();
        var player = new Player
        {
            NameNormalized = normalized,
            DisplayName = displayName.Trim(),
            CodeHmac = hasher.Hash(code),
            CreatedAt = now,
            ReservedUntil = now + ReservationTtl
        };
        db.Players.Add(player);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return new(ReserveResult.Taken); } // lost a race on the unique index

        return new(ReserveResult.Created, player, code);
    }

    /// <summary>Step 2: player retypes the code, which finalizes the registration.</summary>
    public async Task<bool> ConfirmAsync(string displayName, string code, CancellationToken ct = default)
    {
        var normalized = NameRules.Normalize(displayName);
        var now = time.GetUtcNow();

        var player = await db.Players.FirstOrDefaultAsync(
            p => p.NameNormalized == normalized && p.ConfirmedAt == null && p.ReservedUntil >= now, ct);

        if (player is null || !hasher.Matches(code, player.CodeHmac)) return false;

        player.ConfirmedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Checks name + code for a confirmed player, with per-name lockout.</summary>
    public async Task<VerifyOutcome> VerifyAsync(string displayName, string code, CancellationToken ct = default)
    {
        var normalized = NameRules.Normalize(displayName);
        var now = time.GetUtcNow();

        var player = await db.Players.FirstOrDefaultAsync(
            p => p.NameNormalized == normalized && p.ConfirmedAt != null, ct);

        if (player is null) return new(VerifyResult.NotFound);
        if (player.LockedUntil > now) return new(VerifyResult.Locked, LockedUntil: player.LockedUntil);

        if (hasher.Matches(code, player.CodeHmac))
        {
            if (player.FailedAttempts != 0 || player.LockedUntil is not null)
            {
                player.FailedAttempts = 0;
                player.LockedUntil = null;
                await db.SaveChangesAsync(ct);
            }
            return new(VerifyResult.Ok, player);
        }

        player.FailedAttempts++;
        if (player.FailedAttempts >= MaxAttempts)
        {
            player.FailedAttempts = 0;
            player.LockedUntil = now + LockDuration;
        }
        await db.SaveChangesAsync(ct);
        return new(VerifyResult.WrongCode, LockedUntil: player.LockedUntil);
    }

    /// <summary>Admin: issue a fresh code (lost-code flow after the person proved who they are).</summary>
    public async Task<string?> ReissueAsync(int playerId, CancellationToken ct = default)
    {
        var player = await db.Players.FindAsync([playerId], ct);
        if (player is null) return null;

        var code = generator.Generate();
        player.CodeHmac = hasher.Hash(code);
        player.FailedAttempts = 0;
        player.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        return code;
    }
}