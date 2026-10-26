using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Algorithms.Sidewinder;

/// <summary>
/// Sidewinder algorithm. Works row by row: builds runs of cells joined to the east, and when a run
/// is closed, carves one random cell of that run to the north. The top row is one open corridor.
/// Expects a draft with all walls.
/// </summary>
public sealed class Sidewinder : IGeneratorAlgorithm
{
    private readonly Random _pseudoRandomNumbers;

    public Sidewinder()
    {
        _pseudoRandomNumbers = new Random();
    }

    public Sidewinder(Random rngesus)
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
        var run = new List<int>();

        for (var y = 0; y < height; y++)
        {
            run.Clear();

            for (var x = 0; x < width; x++)
            {
                run.Add(x);

                var atEastEdge = x == width - 1;

                if (y == 0)
                {
                    // Nothing above the top row, so it is always a single corridor.
                    if (!atEastEdge)
                    {
                        Carve(draft, At(y, x), At(y, x + 1));
                    }

                    continue;
                }

                var closeRun = atEastEdge || _pseudoRandomNumbers.Next(2) == 0;

                if (closeRun)
                {
                    var chosen = run[_pseudoRandomNumbers.Next(run.Count)];
                    Carve(draft, At(y, chosen), At(y - 1, chosen));
                    run.Clear();
                }
                else
                {
                    Carve(draft, At(y, x), At(y, x + 1));
                }
            }
        }

        draft.PlaceRandomEntranceAndExit(_pseudoRandomNumbers);

        return draft;
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
}