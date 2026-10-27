using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Algorithms.Ellers;

/// <summary>
/// Eller's algorithm. Builds the maze one row at a time while only remembering the set id of each
/// cell in the current row. For each row: randomly join neighbouring cells of different sets, then
/// carve downwards from every set at least once. The last row joins every remaining set.
/// Expects a draft with all walls.
/// </summary>
public sealed class Ellers : IGeneratorAlgorithm
{
    private readonly Random _pseudoRandomNumbers;

    public Ellers()
    {
        _pseudoRandomNumbers = new Random();
    }

    public Ellers(Random rngesus)
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

        var nextSetId = 0;
        var sets = new int[width];
        for (var x = 0; x < width; x++)
        {
            sets[x] = nextSetId++;
        }

        for (var y = 0; y < height; y++)
        {
            var isLastRow = y == height - 1;

            // 1. Join neighbours that are in different sets (always on the last row).
            for (var x = 0; x < width - 1; x++)
            {
                if (sets[x] == sets[x + 1])
                {
                    continue;
                }

                if (isLastRow || _pseudoRandomNumbers.Next(2) == 0)
                {
                    Carve(draft, At(y, x), At(y, x + 1));
                    Merge(sets, from: sets[x + 1], into: sets[x]);
                }
            }

            if (isLastRow)
            {
                break;
            }

            // 2. Carve down: every set gets at least one connection, others by coin flip.
            var groups = new SortedDictionary<int, List<int>>();
            for (var x = 0; x < width; x++)
            {
                if (!groups.TryGetValue(sets[x], out var members))
                {
                    members = [];
                    groups[sets[x]] = members;
                }

                members.Add(x);
            }

            var goesDown = new bool[width];
            foreach (var members in groups.Values)
            {
                Shuffle(members);
                goesDown[members[0]] = true;

                for (var i = 1; i < members.Count; i++)
                {
                    goesDown[members[i]] = _pseudoRandomNumbers.Next(2) == 0;
                }
            }

            // 3. Build the next row's sets: cells reached from above keep their set, the rest are new.
            var nextRow = new int[width];
            for (var x = 0; x < width; x++)
            {
                if (goesDown[x])
                {
                    Carve(draft, At(y, x), At(y + 1, x));
                    nextRow[x] = sets[x];
                }
                else
                {
                    nextRow[x] = nextSetId++;
                }
            }

            sets = nextRow;
        }

        draft.PlaceRandomEntranceAndExit(_pseudoRandomNumbers);

        return draft;
    }

    private static void Merge(int[] sets, int from, int into)
    {
        for (var i = 0; i < sets.Length; i++)
        {
            if (sets[i] == from)
            {
                sets[i] = into;
            }
        }
    }

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
}