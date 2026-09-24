namespace CodeMaze.MazeGeneration.Maze;

public readonly record struct XCoordinate(int Value)
{
    public static XCoordinate operator +(XCoordinate first, XCoordinate second)
    {
        return new XCoordinate(first.Value + second.Value);
    }

    public static XCoordinate operator +(XCoordinate self, int offset)
    {
        return new XCoordinate(self.Value + offset);
    }

    public static XCoordinate operator -(XCoordinate first, XCoordinate second)
    {
        return new XCoordinate(first.Value - second.Value);
    }

    public static XCoordinate operator -(XCoordinate self, int offset)
    {
        return new XCoordinate(self.Value - offset);
    }
}