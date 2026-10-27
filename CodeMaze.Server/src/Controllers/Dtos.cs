using System.ComponentModel.DataAnnotations;

namespace CodeMaze.Server.Controllers;

public record ReserveRequest([Required, StringLength(32)] string DisplayName);
public record CodeRequest([Required, StringLength(32)] string DisplayName, [Required, StringLength(64)] string Code);
public record SubmitScoreRequest(
    [Required, StringLength(32)] string DisplayName,
    [Required, StringLength(64)] string Code,
    int Score,
    [Required, StringLength(20000)] string Program);

public record ReserveResponse(int PlayerId, string PublicId, string DisplayName, string Code, Guid SessionId);
public record LeaderboardEntry(int Rank, string PublicId, string DisplayName, int Score, DateTimeOffset AchievedAt);