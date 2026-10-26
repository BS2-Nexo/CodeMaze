using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Algorithms.Wilsons;

/// <summary>
/// Wilson's algorithm (loop-erased random walks). Generates an unbiased, uniformly random maze.
/// Starts with one random cell in the maze, then walks randomly from unvisited cells until the
/// walk hits the maze, erasing any loops, and carves the resulting path. Expects a draft with all walls.
/// </summary>
public sealed class Wilsons : IGeneratorAlgorithm
{
    private static readonly (int Dy, int Dx)[] Directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    private readonly Random _pseudoRandomNumbers;

    public Wilsons()
    {
        _pseudoRandomNumbers = new Random();
    }

    public Wilsons(Random rngesus)
    {
        _pseudoRandomNumbers = rngesus;
    }

    public DraftMaze Generate(int height, int width)
    {
        var draft = new DraftMaze(height, width, Walls.All);
        return Generate(draft);
    }

    public DraftMaze Generate(DraftMaze draft)
    {
        var width = draft.Width;
        var height = draft.Height;
        var count = width * height;

        var inMaze = new bool[count];
        var next = new int[count]; // where the walk last left each cell; overwriting this erases loops

        inMaze[_pseudoRandomNumbers.Next(count)] = true;

        // The order in which walks start does not change the distribution, so a shuffled pass is fine.
        var order = Enumerable.Range(0, count).ToArray();
        Shuffle(order);

        foreach (var start in order)
        {
            if (inMaze[start])
            {
                continue;
            }

            // Random walk until we reach the maze.
            var cell = start;
            while (!inMaze[cell])
            {
                var neighbor = RandomNeighbor(cell, width, height);
                next[cell] = neighbor;
                cell = neighbor;
            }

            // Follow the loop-erased path and carve it.
            cell = start;
            while (!inMaze[cell])
            {
                inMaze[cell] = true;
                Carve(draft, ToPosition(cell, width), ToPosition(next[cell], width));
                cell = next[cell];
            }
        }

        draft.PlaceRandomEntranceAndExit(_pseudoRandomNumbers);

        return draft;
    }

    private int RandomNeighbor(int cell, int width, int height)
    {
        var y = cell / width;
        var x = cell % width;

        Span<int> candidates = stackalloc int[4];
        var n = 0;

        foreach (var (dy, dx) in Directions)
        {
            var ny = y + dy;
            var nx = x + dx;
            if (ny >= 0 && ny < height && nx >= 0 && nx < width)
            {
                candidates[n++] = ny * width + nx;
            }
        }

        return candidates[_pseudoRandomNumbers.Next(n)];
    }

    private void Shuffle(int[] items)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = _pseudoRandomNumbers.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static CellPosition ToPosition(int index, int width) => At(index / width, index % width);

    private static CellPosition At(int y, int x) => new(new YCoordinate(y), new XCoordinate(x));

    private static void Carve(DraftMaze draft, CellPosition a, CellPosition b)
    {
        var dy = b.Y.Value - a.Y.Value;
        var dx = b.X.Value - a.X.Value;

        var (aWall, bWall) = (dy, dx) switch
        {
            (-1, 0) => (Walls.Top, Walls.Bottom),
            (1, 0) => (Walls.Bottom, Walls.Top),
            (0, -1) => (Walls.Left, Walls.Right),
            (0, 1) => (Walls.Right, Walls.Left),
            _ => throw new ArgumentException("Cells must be adjacent."),
        };

        draft[a] &= ~aWall;
        draft[b] &= ~bWall;
    }
}