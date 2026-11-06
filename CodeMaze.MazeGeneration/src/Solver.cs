namespace CodeMaze.MazeGeneration;

public static class MazeSolver
{
    private static readonly (Walls Side, int Dy, int Dx)[] Moves =
    [
        (Walls.Top,    -1,  0),
        (Walls.Right,   0,  1),
        (Walls.Bottom,  1,  0),
        (Walls.Left,    0, -1),
    ];

    /// <summary>
    /// Shortest path from Entrance to Exit, both inclusive, in visiting order.
    /// Returns null if the exit cannot be reached.
    /// A step is allowed when the current cell has no wall on that side and the target is inside the maze.
    /// </summary>
    public static IReadOnlyList<CellPosition>? FindShortestPath(Maze maze)
    {
        ArgumentNullException.ThrowIfNull(maze);

        int w = maze.Width, h = maze.Height;
        var cells = maze.Cells.ToArray();

        int start = maze.Entrance.Y.Value * w + maze.Entrance.X.Value;
        int goal = maze.Exit.Y.Value * w + maze.Exit.X.Value;

        const int Unvisited = -2, Origin = -1;
        var prev = new int[w * h];
        Array.Fill(prev, Unvisited);
        prev[start] = Origin;

        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (current == goal) break;

            int cy = current / w, cx = current % w;

            foreach (var (side, dy, dx) in Moves)
            {
                if (cells[current].HasFlag(side)) continue;

                int ny = cy + dy, nx = cx + dx;
                if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;

                int next = ny * w + nx;
                if (prev[next] != Unvisited) continue;

                prev[next] = current;
                queue.Enqueue(next);
            }
        }

        if (prev[goal] == Unvisited) return null;

        var path = new List<CellPosition>();
        for (int i = goal; i != Origin; i = prev[i])
        {
            path.Add(new CellPosition(new YCoordinate(i / w), new XCoordinate(i % w)));
        }

        path.Reverse();
        return path;
    }


    /// <summary>
    /// Returns true if at least one path exists from Entrance to Exit of the draft.
    /// A step is allowed when the current cell has no wall on that side and the target is inside the maze.
    /// </summary>
    public static bool HasPath(DraftMaze draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return HasPath(draft.GetCells(), draft.Width, draft.Height, draft.Entrance, draft.Exit);
    }

    /// <summary>
    /// Returns true if at least one path exists from Entrance to Exit of the maze.
    /// </summary>
    public static bool HasPath(Maze maze)
    {
        ArgumentNullException.ThrowIfNull(maze);

        return HasPath(maze.Cells.ToArray(), maze.Width, maze.Height, maze.Entrance, maze.Exit);
    }

    private static bool HasPath(Walls[] cells, int w, int h, CellPosition entrance, CellPosition exit)
    {
        int start = entrance.Y.Value * w + entrance.X.Value;
        int goal = exit.Y.Value * w + exit.X.Value;

        if (start == goal) return true;

        var visited = new bool[w * h];
        visited[start] = true;

        var stack = new Stack<int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            int current = stack.Pop();
            int cy = current / w, cx = current % w;

            foreach (var (side, dy, dx) in Moves)
            {
                if (cells[current].HasFlag(side)) continue;

                int ny = cy + dy, nx = cx + dx;
                if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;

                int next = ny * w + nx;
                if (visited[next]) continue;

                if (next == goal) return true;

                visited[next] = true;
                stack.Push(next);
            }
        }

        return false;
    }
}