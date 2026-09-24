namespace CodeMaze.MazeGeneration.Maze;

[Flags]
public enum Walls : byte
{
    None = 0,

    /// <summary>
    /// The wall on the north side of the cell.
    /// North is toward decreasing Y coordinates.
    /// </summary>
    Top = 1 << 0,

    /// <summary>
    /// The wall on the east side of the cell.
    /// East is toward increasing X coordinates.
    /// </summary>
    Right = 1 << 1,

    /// <summary>
    /// The wall on the south side of the cell.
    /// South is toward increasing Y coordinates.
    /// </summary>
    Bottom = 1 << 2,

    /// <summary>
    /// The wall on the west side of the cell.
    /// West is toward decreasing X coordinates.
    /// </summary>
    Left = 1 << 3,

    All = Top | Right | Bottom | Left
}
