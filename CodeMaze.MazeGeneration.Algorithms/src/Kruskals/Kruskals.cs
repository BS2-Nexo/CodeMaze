using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Algorithms.Kruskals;

/// <summary>
/// Randomized Kruskal's algorithm. Shuffles every wall between two cells and removes it when the
/// two cells are not yet connected (tracked with a union-find). Expects a draft with all walls.
/// </summary>
public sealed class Kruskals : IGeneratorAlgorithm
{
    private readonly Random _pseudoRandomNumbers;

    public Kruskals()
    {
        _pseudoRandomNumbers = new Random();
    }

    public Kruskals(Random rngesus)
    {
        _pseudoRandomNumbers = rngesus;
    }

    public DraftMaze Generate(int height, int width, Difficulty difficulty)
    {
        var draft = new DraftMaze(height, width, Walls.All, difficulty);
        return Generate(draft);
    }

    public DraftMaze Generate(DraftMaze draft)
    {
        var width = draft.Width;
        var height = draft.Height;

        // Every internal wall once: each cell owns its east and its south wall.
        var edges = new List<(int Y, int X, bool East)>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x < width - 1) edges.Add((y, x, true));
                if (y < height - 1) edges.Add((y, x, false));
            }
        }

        Shuffle(edges);

        var sets = new DisjointSets(width * height);

        foreach (var (y, x, east) in edges)
        {
            var (ny, nx) = east ? (y, x + 1) : (y + 1, x);

            if (sets.Union(y * width + x, ny * width + nx))
            {
                Carve(draft, At(y, x), At(ny, nx));
            }
        }

        draft.PlaceRandomEntranceAndExit(_pseudoRandomNumbers);

        return draft;
    }

    // Fisher-Yates
    private void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _pseudoRandomNumbers.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

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

    /// <summary>Union-find with path halving and union by size.</summary>
    private sealed class DisjointSets
    {
        private readonly int[] _parent;
        private readonly int[] _size;

        public DisjointSets(int count)
        {
            _parent = new int[count];
            _size = new int[count];
            for (var i = 0; i < count; i++)
            {
                _parent[i] = i;
                _size[i] = 1;
            }
        }

        private int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }

            return i;
        }

        /// <summary>Joins the sets of a and b. Returns false if they were already the same set.</summary>
        public bool Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);

            if (rootA == rootB)
            {
                return false;
            }

            if (_size[rootA] < _size[rootB])
            {
                (rootA, rootB) = (rootB, rootA);
            }

            _parent[rootB] = rootA;
            _size[rootA] += _size[rootB];
            return true;
        }
    }
}