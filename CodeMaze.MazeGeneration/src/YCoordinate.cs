namespace CodeMaze.MazeGeneration;

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

    public static bool operator >(YCoordinate first, YCoordinate second)
    {
        return first.Value > second.Value;
    }

    public static bool operator <(YCoordinate first, YCoordinate second)
    {
        return first.Value < second.Value;
    }

    public static bool operator >=(YCoordinate first, YCoordinate second)
    {
        return first.Value > second.Value || first.Value == second.Value;
    }

    public static bool operator <=(YCoordinate first, YCoordinate second)
    {
        return first.Value < second.Value || first.Value == second.Value;
    }

    public static bool operator >(YCoordinate first, int second)
    {
        return first.Value > second;
    }

    public static bool operator <(YCoordinate first, int second)
    {
        return first.Value < second;
    }

    public static bool operator >=(YCoordinate first, int second)
    {
        return first.Value > second || first.Value == second;
    }

    public static bool operator <=(YCoordinate first, int second)
    {
        return first.Value < second || first.Value == second;
    }

    public static bool operator >(int first, YCoordinate second)
    {
        return first > second.Value;
    }

    public static bool operator <(int first, YCoordinate second)
    {
        return first < second.Value;
    }

    public static bool operator >=(int first, YCoordinate second)
    {
        return first > second.Value || first == second.Value;
    }

    public static bool operator <=(int first, YCoordinate second)
    {
        return first < second.Value || first == second.Value;
    }
}