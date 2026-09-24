namespace CodeMaze.MazeGeneration.Maze;

public readonly record struct CellPosition(YCoordinate Y, XCoordinate X)
{
    public static CellPosition operator +(CellPosition first, CellPosition second)
    {
        return new CellPosition(first.Y + second.Y, first.X + second.X);
    }

    public static CellPosition operator -(CellPosition first, CellPosition second)
    {
        return new CellPosition(first.Y - second.Y, first.X - second.X);
    }
}