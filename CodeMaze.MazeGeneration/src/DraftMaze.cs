namespace CodeMaze.MazeGeneration;

public sealed class DraftMaze
{
    private Dictionary<CellPosition, Walls> Cells { get; set; } = [];
    public int Width { get; private set; }
    public int Height { get; private set; }
    public CellPosition Entrance { get; set; }
    public CellPosition Exit { get; set; }
    public Difficulty Difficulty { get; }


    public DraftMaze(int height, int width, Walls initialWallLayout, Difficulty difficulty)
    {
        Width = width;
        Height = height;
        Difficulty = difficulty;

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
    public (CellPosition Entrance, CellPosition Exit) PlaceRandomEntranceAndExit(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var entrance = new CellPosition(new YCoordinate(0), new XCoordinate(random.Next(Width)));
        var exit = new CellPosition(new YCoordinate(Height - 1), new XCoordinate(random.Next(Width)));

        PlaceEntranceAndExit(entrance, exit);
        return (entrance, exit);
    }

    /// <summary>
    /// Sets the entrance and exit to the given cells. Walls are not touched.
    /// </summary>
    public void PlaceEntranceAndExit(CellPosition entrance, CellPosition exit)
    {
        BoundsCheck(entrance);
        BoundsCheck(exit);

        if (entrance == exit)
        {
            throw new ArgumentException("Entrance and exit must be different cells.", nameof(exit));
        }

        Entrance = entrance;
        Exit = exit;
    }


    private static readonly (Walls Side, Walls Opposite, int Dy, int Dx)[] Sides =
    [
        (Walls.Top,    Walls.Bottom, -1,  0),
        (Walls.Bottom, Walls.Top,     1,  0),
        (Walls.Left,   Walls.Right,   0, -1),
        (Walls.Right,  Walls.Left,    0,  1),
    ];

    /// <summary>
    /// Places the entrance and exit anywhere in the maze. Walls are not touched, so call this
    /// after the passages have been carved.
    ///
    /// - The exit is a dead end: a cell with exactly one open side, so it sits at the end of a path.
    /// - The entrance is any other cell with 1 to 3 open sides (never a 4-way junction) that is
    ///   connected to the exit, so the exit is always reachable from the entrance.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No dead end has a connected cell to use as the entrance (for example when the maze has not
    /// been carved yet), or the walls of the maze are inconsistent.
    /// </exception>
    public (CellPosition Entrance, CellPosition Exit) PlaceRandomEntranceAndExitAnywhere(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        // Row-major scan keeps the order stable, so a seeded Random is reproducible.
        var deadEnds = new List<CellPosition>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pos = new CellPosition(new YCoordinate(y), new XCoordinate(x));
                if (CountOpenSides(Cells[pos]) == 1)
                {
                    deadEnds.Add(pos);
                }
            }
        }

        // Try dead ends in random order until one has a connected cell for the entrance.
        // In a normal carved maze the first one works.
        while (deadEnds.Count > 0)
        {
            var i = random.Next(deadEnds.Count);
            var exit = deadEnds[i];
            deadEnds.RemoveAt(i);

            var entranceCandidates = Flood(exit, requireBothSides: true)
                .Where(p => p != exit && CountOpenSides(Cells[p]) is >= 1 and <= 3)
                .OrderBy(p => p.Y.Value).ThenBy(p => p.X.Value)   // stable order for seeded Random
                .ToList();

            if (entranceCandidates.Count == 0)
            {
                continue;
            }

            var entrance = entranceCandidates[random.Next(entranceCandidates.Count)];

            // Final check with the same rule the interpreter uses (only the wall of the current cell).
            if (!IsReachable(entrance, exit))
            {
                throw new InvalidOperationException(
                    $"The exit {exit} cannot be reached from the entrance {entrance}. The walls are inconsistent.");
            }

            PlaceEntranceAndExit(entrance, exit);
            return (entrance, exit);
        }

        throw new InvalidOperationException(
            "The maze has no dead end connected to another suitable cell. Carve the passages first.");
    }

    /// <summary>Number of sides of a cell without a wall (the outer boundary counts as walls).</summary>
    private static int CountOpenSides(Walls walls)
    {
        var open = 0;
        if (!walls.HasFlag(Walls.Top)) open++;
        if (!walls.HasFlag(Walls.Right)) open++;
        if (!walls.HasFlag(Walls.Bottom)) open++;
        if (!walls.HasFlag(Walls.Left)) open++;
        return open;
    }

    /// <summary>
    /// True if a program can walk from one cell to the other. Uses the interpreter's rule:
    /// a step is possible when the current cell has no wall on that side and the target is inside the maze.
    /// </summary>
    public bool IsReachable(CellPosition from, CellPosition to)
    {
        BoundsCheck(from);
        BoundsCheck(to);
        return Flood(from, requireBothSides: false).Contains(to);
    }

    /// <summary>Breadth-first flood fill. With requireBothSides, a passage must be open on both cells.</summary>
    private HashSet<CellPosition> Flood(CellPosition start, bool requireBothSides)
    {
        var seen = new HashSet<CellPosition> { start };
        var queue = new Queue<CellPosition>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var walls = Cells[current];

            foreach (var (side, opposite, dy, dx) in Sides)
            {
                if (walls.HasFlag(side))
                {
                    continue;
                }

                var next = new CellPosition(
                    new YCoordinate(current.Y.Value + dy),
                    new XCoordinate(current.X.Value + dx));

                if (!Contains(next) || (requireBothSides && Cells[next].HasFlag(opposite)))
                {
                    continue;
                }

                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return seen;
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
        var maze = new Maze(this);
        var shortestPath = MazeSolver.FindShortestPath(maze);
        maze.ShortestPath = shortestPath;
        return maze;
    }

}