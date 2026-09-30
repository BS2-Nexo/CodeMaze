namespace CodeMaze.MazeGeneration;

public sealed class DraftMaze
{
    private Dictionary<CellPosition, Walls> Cells { get; set; } = [];
    public int Width { get; private set; }
    public int Height { get; private set; }
    public CellPosition Entrance { get; private set; }
    public CellPosition Exit { get; private set; }


    public DraftMaze(int height, int width, Walls initialWallLayout)
    {
        Width = width;
        Height = height;

        foreach (var yCoord in Enumerable.Range(0, Height).Select(y => new YCoordinate(y)))
        {
            foreach (var xCoord in Enumerable.Range(0, Width).Select(x => new XCoordinate(x)))
            {
                var pos = new CellPosition(yCoord, xCoord);
                var walls = GetBoundaryWalls(pos);

                if (walls == Walls.None || initialWallLayout == Walls.All)
                {
                    walls = initialWallLayout;
                }

                Cells.Add(pos, walls);
            }
        }
    }

    public CellPositionMover GetMover()
    {
        return new CellPositionMover(BoundsCheck);
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
    public (CellPosition Entrance, CellPosition Exit) OpenEntranceAndExit(Random random)
    {
        var entrance = new CellPosition(new YCoordinate(0), new XCoordinate(random.Next(Width)));
        var exit = new CellPosition(new YCoordinate(Height - 1), new XCoordinate(random.Next(Width)));

        this[entrance] &= ~Walls.Top;
        this[exit] &= ~Walls.Bottom;

        return (entrance, exit);
    }

    public Walls[] GetCells()
    {
        var cells = new Walls[Width * Height];

        foreach (var (position, walls) in Cells)
        {
            cells[position.Y.Value * Width + position.X.Value] = walls;
        }

        return cells;
    }

    public Maze Finalize()
    {
        return new Maze(this);
    }

}