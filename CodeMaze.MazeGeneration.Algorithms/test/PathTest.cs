using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;
using Ellers = CodeMaze.MazeGeneration.Algorithms.Ellers.Ellers;
using Kruskals = CodeMaze.MazeGeneration.Algorithms.Kruskals.Kruskals;
using Prims = CodeMaze.MazeGeneration.Algorithms.Prims.Prims;
using RecursiveBacktracker = CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker.RecursiveBacktracker;
using RecursiveDivision = CodeMaze.MazeGeneration.Algorithms.RecursiveDivision.RecursiveDivision;
using Sidewinder = CodeMaze.MazeGeneration.Algorithms.Sidewinder.Sidewinder;
using Wilsons = CodeMaze.MazeGeneration.Algorithms.Wilsons.Wilsons;
using Xunit;

namespace CodeMaze.AlgorithmTests;

public class PathTests
{
    private static readonly Dictionary<string, Func<Random, IGeneratorAlgorithm>> Algorithms = new()
    {
        ["Ellers"] = rng => new Ellers(rng),
        ["Kruskals"] = rng => new Kruskals(rng),
        ["Prims"] = rng => new Prims(rng),
        ["RecursiveBacktracker"] = rng => new RecursiveBacktracker(rng),
        ["RecursiveDivision"] = rng => new RecursiveDivision(rng),
        ["Sidewinder"] = rng => new Sidewinder(rng),
        ["Wilsons"] = rng => new Wilsons(rng),
    };

    // Minimum is 2 rows: PlaceRandomEntranceAndExit uses the top and bottom row, so 1 row can
    // put both on the same cell.
    private static readonly (int Height, int Width)[] Sizes =
        [(2, 2), (2, 15), (15, 2), (10, 10), (5, 20), (20, 5), (30, 30)];

    public static IEnumerable<object[]> AlgorithmNames =>
        Algorithms.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(AlgorithmNames))]
    public void GeneratedMazes_AreSolvableConsistentAndPerfect(string name)
    {
        foreach (var (height, width) in Sizes)
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var context = $"{name}, seed {seed}, {height}x{width}";
                var draft = Algorithms[name](new Random(seed)).Generate(height, width, default);

                AssertEntranceAndExit(draft, context);
                AssertBoundaryClosed(draft, context);
                AssertWallsConsistent(draft, context);
                AssertPerfect(draft, context);

                Assert.True(MazeSolver.HasPath(draft), $"No path from entrance to exit ({context}).");
            }
        }
    }

    [Fact]
    public void EnsurePath_ConnectsAMazeWithNoPath()
    {
        // All walls closed: nothing is reachable.
        var draft = new DraftMaze(5, 5, Walls.All, default);
        draft.PlaceEntranceAndExit(
            new CellPosition(new YCoordinate(0), new XCoordinate(0)),
            new CellPosition(new YCoordinate(4), new XCoordinate(4)));

        Assert.False(MazeSolver.HasPath(draft));

        var opened = draft.EnsurePath();

        Assert.True(opened > 0);
        Assert.True(MazeSolver.HasPath(draft));
        Assert.Equal(0, draft.EnsurePath()); // second call changes nothing
    }

    private static CellPosition At(int y, int x) => new(new YCoordinate(y), new XCoordinate(x));

    private static void AssertEntranceAndExit(DraftMaze draft, string context)
    {
        Assert.True(draft.Contains(draft.Entrance), $"Entrance outside the maze ({context}).");
        Assert.True(draft.Contains(draft.Exit), $"Exit outside the maze ({context}).");
        Assert.NotEqual(draft.Entrance, draft.Exit);
        Assert.Equal(0, draft.Entrance.Y.Value);
        Assert.Equal(draft.Height - 1, draft.Exit.Y.Value);
    }

    // Nothing may open the outer boundary: entrance and exit are only marked, not carved.
    private static void AssertBoundaryClosed(DraftMaze draft, string context)
    {
        for (var y = 0; y < draft.Height; y++)
        {
            for (var x = 0; x < draft.Width; x++)
            {
                var cell = draft[At(y, x)];

                if (y == 0) Assert.True(cell.HasFlag(Walls.Top), $"Open top boundary at {y},{x} ({context}).");
                if (y == draft.Height - 1) Assert.True(cell.HasFlag(Walls.Bottom), $"Open bottom boundary at {y},{x} ({context}).");
                if (x == 0) Assert.True(cell.HasFlag(Walls.Left), $"Open left boundary at {y},{x} ({context}).");
                if (x == draft.Width - 1) Assert.True(cell.HasFlag(Walls.Right), $"Open right boundary at {y},{x} ({context}).");
            }
        }
    }

    // A wall between two cells must be present on both sides or on neither.
    private static void AssertWallsConsistent(DraftMaze draft, string context)
    {
        for (var y = 0; y < draft.Height; y++)
        {
            for (var x = 0; x < draft.Width; x++)
            {
                var cell = draft[At(y, x)];

                if (x < draft.Width - 1)
                {
                    var right = draft[At(y, x + 1)];
                    Assert.True(
                        cell.HasFlag(Walls.Right) == right.HasFlag(Walls.Left),
                        $"Inconsistent wall between {y},{x} and {y},{x + 1} ({context}).");
                }

                if (y < draft.Height - 1)
                {
                    var below = draft[At(y + 1, x)];
                    Assert.True(
                        cell.HasFlag(Walls.Bottom) == below.HasFlag(Walls.Top),
                        $"Inconsistent wall between {y},{x} and {y + 1},{x} ({context}).");
                }
            }
        }
    }

    // Every algorithm builds a spanning tree: all cells reachable and exactly cells - 1 passages.
    // More passages means loops, fewer means isolated areas.
    private static void AssertPerfect(DraftMaze draft, string context)
    {
        var w = draft.Width;
        var h = draft.Height;
        var total = w * h;

        var passages = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var cell = draft[At(y, x)];
                if (x < w - 1 && !cell.HasFlag(Walls.Right)) passages++;
                if (y < h - 1 && !cell.HasFlag(Walls.Bottom)) passages++;
            }
        }

        Assert.True(passages == total - 1, $"Expected {total - 1} passages but found {passages} ({context}).");

        var seen = new bool[total];
        var queue = new Queue<(int Y, int X)>();
        seen[draft.Entrance.Y.Value * w + draft.Entrance.X.Value] = true;
        queue.Enqueue((draft.Entrance.Y.Value, draft.Entrance.X.Value));
        var reached = 1;

        while (queue.Count > 0)
        {
            var (y, x) = queue.Dequeue();
            var cell = draft[At(y, x)];

            var steps = new[]
            {
                (Walls.Top, y - 1, x),
                (Walls.Bottom, y + 1, x),
                (Walls.Left, y, x - 1),
                (Walls.Right, y, x + 1),
            };

            foreach (var (side, ny, nx) in steps)
            {
                if (cell.HasFlag(side) || ny < 0 || ny >= h || nx < 0 || nx >= w || seen[ny * w + nx])
                {
                    continue;
                }

                seen[ny * w + nx] = true;
                reached++;
                queue.Enqueue((ny, nx));
            }
        }

        Assert.True(reached == total, $"Only {reached} of {total} cells are reachable ({context}).");
    }
}