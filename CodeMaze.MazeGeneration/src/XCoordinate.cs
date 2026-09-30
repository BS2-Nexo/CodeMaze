namespace CodeMaze.MazeGeneration;

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

    public static bool operator >(XCoordinate first, XCoordinate second)
    {
        return first.Value > second.Value;
    }

    public static bool operator <(XCoordinate first, XCoordinate second)
    {
        return first.Value < second.Value;
    }

    public static bool operator >=(XCoordinate first, XCoordinate second)
    {
        return first.Value > second.Value || first.Value == second.Value;
    }

    public static bool operator <=(XCoordinate first, XCoordinate second)
    {
        return first.Value < second.Value || first.Value == second.Value;
    }

    public static bool operator >(XCoordinate first, int second)
    {
        return first.Value > second;
    }

    public static bool operator <(XCoordinate first, int second)
    {
        return first.Value < second;
    }

    public static bool operator >=(XCoordinate first, int second)
    {
        return first.Value > second || first.Value == second;
    }

    public static bool operator <=(XCoordinate first, int second)
    {
        return first.Value < second || first.Value == second;
    }

    public static bool operator >(int first, XCoordinate second)
    {
        return first > second.Value;
    }

    public static bool operator <(int first, XCoordinate second)
    {
        return first < second.Value;
    }

    public static bool operator >=(int first, XCoordinate second)
    {
        return first > second.Value || first == second.Value;
    }

    public static bool operator <=(int first, XCoordinate second)
    {
        return first < second.Value || first == second.Value;
    }
}