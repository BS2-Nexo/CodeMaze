namespace CodeMaze.MazeLang.Core;

/// <summary>
/// Direction in the maze. Shared by the AST and the IL so that both use the
/// same vocabulary (and nothing else in common).
/// </summary>
public enum Direction
{
    Up,
    Down,
    Left,
    Right,

    /// <summary>
    /// Go to the previous cell (history). Not a real direction:
    /// only allowed as the target of a move, never for sensors or function arguments.
    /// </summary>
    Back,
}
