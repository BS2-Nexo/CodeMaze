using CodeMaze.MazeLang.Core;
namespace CodeMaze.MazeLang.Interpreter;

public static class DirectionExtensions
{
    /// <summary>
    /// The only place that knows the concrete Direction members. ADJUST the member names
    /// here if your enum differs (hoch/runter/links/rechts = Up/Down/Left/Right assumed).
    /// </summary>
    public static (int Dy, int Dx) Delta(this Direction d) => d switch
    {
        Direction.Up => (-1, 0),
        Direction.Down => (1, 0),
        Direction.Left => (0, -1),
        Direction.Right => (0, 1),
        _ => throw new InvalidOperationException($"'{d}' is not a physical direction."),
    };
}