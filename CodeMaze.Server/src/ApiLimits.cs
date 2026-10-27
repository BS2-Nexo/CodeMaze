namespace CodeMaze.Server;

/// <summary>Single source of truth for the limits the API enforces and advertises via /api/capabilities.</summary>
public static class ApiLimits
{
    public const int MinMazeSize = 2;
    public const int MaxMazeSize = 100;
    public const long MaxMazeCells = MaxMazeSize * MaxMazeSize;

    public const int MaxCodeLength = 20_000;
    public const int MaxLanguageDefinitionLength = 20_000;

    public const int MinStepDelayMs = 0;
    public const int DefaultStepDelayMs = 100;
    public const int MaxStepDelayMs = 2_000;
}