namespace CodeMaze.MazeGeneration.Solving;

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
}