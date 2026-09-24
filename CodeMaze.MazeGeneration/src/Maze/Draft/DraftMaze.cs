namespace CodeMaze.MazeGeneration.Maze.Draft;

public sealed class DraftMaze
{
    private Dictionary<CellPosition, Walls> Cells { get; set; } = [];
    private int Width { get; set; }
    private int Height { get; set; }

    private readonly CellPositionMover mover;

    public DraftMaze(int height, int width, Walls initialWallLayout)
    {
        Width = width;
        Height = height;

        mover = new(BoundsCheck);

        foreach (var yCoord in Enumerable.Range(0, Height).Select(y => new YCoordinate(y)))
        {
            foreach (var xCoord in Enumerable.Range(0, Width).Select(x => new XCoordinate(x)))
            {
                var pos = new CellPosition(yCoord, xCoord);
                var walls = GetBoundaryWalls(pos);

                if (walls == Walls.None)
                {
                    walls = initialWallLayout;
                }

                Cells.Add(pos, walls);
            }
        }
    }

    public Walls this[CellPosition position]
    {
        get
        {
            BoundsCheck(position);
            return Cells[position];
        }
        set
        {
            BoundsCheck(position);
            Cells[position] = value;
        }
    }

    public Walls this[YCoordinate y, XCoordinate x]
    {
        get => this[new CellPosition(y, x)];
        set => this[new CellPosition(y, x)] = value;
    }

    public bool Contains(CellPosition position)
    {
        return position.X.Value >= 0
            && position.X.Value < Width
            && position.Y.Value >= 0
            && position.Y.Value < Height;
    }

    private void BoundsCheck(CellPosition position)
    {
        var contains = Contains(position);
        if (!contains)
        {
            throw new ArgumentOutOfRangeException(nameof(position),
                $"Position {position} is outside the maze.");
        }
    }

    private Walls GetBoundaryWalls(CellPosition position)
    {
        Walls walls = Walls.None;

        if (position.Y.Value == 0)
        {
            walls |= Walls.Top;
        }

        if (position.X.Value == Width - 1)
        {
            walls |= Walls.Right;
        }

        if (position.Y.Value == Height - 1)
        {
            walls |= Walls.Bottom;
        }

        if (position.X.Value == 0)
        {
            walls |= Walls.Left;
        }

        return walls;
    }
}