namespace CodeMaze.MazeGeneration.Maze;

public readonly record struct YCoordinate(int Value)
{

    public static YCoordinate operator +(YCoordinate first, YCoordinate second)
    {
        return new YCoordinate(first.Value + second.Value);
    }

    public static YCoordinate operator +(YCoordinate self, int offset)
    {
        return new YCoordinate(self.Value + offset);
    }

    public static YCoordinate operator -(YCoordinate first, YCoordinate second)
    {
        return new YCoordinate(first.Value - second.Value);
    }

    public static YCoordinate operator -(YCoordinate self, int offset)
    {
        return new YCoordinate(self.Value - offset);
    }
}