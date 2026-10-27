namespace CodeMaze.Server.Data.Entities;

public class Player
{
    public int Id { get; set; }                       // public ID on the board: #042
    public string NameNormalized { get; set; } = "";  // unique key
    public string DisplayName { get; set; } = "";
    public byte[] CodeHmac { get; set; } = [];        // never leaves the server

    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ReservedUntil { get; set; } // unconfirmed reservations expire
    public DateTimeOffset? ConfirmedAt { get; set; }  // null = still pending

    public List<Score> Scores { get; set; } = [];
}