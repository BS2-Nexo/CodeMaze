using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Algorithms.Prims;

/// <summary>
/// Randomized Prim's algorithm. Grows the maze from one cell by repeatedly picking a random
/// passage from the "frontier" (maze cell -> unvisited neighbour) and carving it.
/// Produces many short dead ends and a lot of branching. Expects a draft with all walls.
/// </summary>
public sealed class Prims : IGeneratorAlgorithm
{
    private static readonly (int Dy, int Dx)[] Directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    private readonly Random _pseudoRandomNumbers;

    public Prims()
    {
        _pseudoRandomNumbers = new Random();
    }

    public Prims(Random rngesus)
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
        var inMaze = new bool[width * height];
        var frontier = new List<(CellPosition From, CellPosition To)>();

        void AddToMaze(CellPosition pos)
        {
            inMaze[pos.Y.Value * width + pos.X.Value] = true;

            foreach (var (dy, dx) in Directions)
            {
                var ny = pos.Y.Value + dy;
                var nx = pos.X.Value + dx;

                if (ny < 0 || ny >= height || nx < 0 || nx >= width || inMaze[ny * width + nx])
                {
                    continue;
                }

                frontier.Add((pos, At(ny, nx)));
            }
        }

        AddToMaze(At(_pseudoRandomNumbers.Next(height), _pseudoRandomNumbers.Next(width)));

        while (frontier.Count > 0)
        {
            // Swap-remove: O(1) and the order of the list does not matter.
            var i = _pseudoRandomNumbers.Next(frontier.Count);
            var (from, to) = frontier[i];
            frontier[i] = frontier[^1];
            frontier.RemoveAt(frontier.Count - 1);

            if (inMaze[to.Y.Value * width + to.X.Value])
            {
                continue;
            }

            Carve(draft, from, to);
            AddToMaze(to);
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