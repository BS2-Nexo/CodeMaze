namespace CodeMaze.MazeGeneration;

public sealed class Cell(int y, int x, Walls walls)
{
    public Walls Walls { get; private set; } = walls;
    public int XCoordinate { get; private set; } = x;
    public int YCoordinate { get; private set; } = y;

    public bool IsDeadEnd => Walls.Count() == 3;
    public bool IsIsolated => Walls.Count() == 4;
}