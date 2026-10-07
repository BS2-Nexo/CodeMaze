using CodeMaze.MazeGeneration;

namespace CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;

public sealed class RecursiveBacktracker
{
    private readonly Random _pseudoRandomNumbers;

    public RecursiveBacktracker(Random rngesus)
    {
        _pseudoRandomNumbers = rngesus;
    }

    public DraftMaze Generate(DraftMaze draft)
    {
        var mover = draft.GetMover();
        var visited = new HashSet<CellPosition>();
        var stack = new Stack<CellPosition>();

        Visit(visited, stack, RandomInBounds(draft));

        while (stack.Count > 0)
        {
            var current = stack.Peek();
            var neighbors = mover.GetNeighborsOf(current);

            if (TryPickUnvisitedNeighbor(neighbors, visited, out var direction, out var next))
            {
                CarvePassage(draft, current, direction, next);
                Visit(visited, stack, next);
            }
            else
            {
                stack.Pop();
            }
        }

        draft.OpenEntranceAndExit(_pseudoRandomNumbers);

        return draft;
    }

    private static void Visit(HashSet<CellPosition> visited, Stack<CellPosition> chain, CellPosition pos)
    {
        visited.Add(pos);
        chain.Push(pos);
    }

    private bool TryPickUnvisitedNeighbor(
        Neighbors neighbors,
        HashSet<CellPosition> visited,
        out CardinalDirection direction,
        out CellPosition position)
    {
        var candidates = neighbors.Valid
            .Where(n => !visited.Contains(n.position!.Value))
            .ToList();

        if (candidates.Count == 0)
        {
            direction = default;
            position = default;
            return false;
        }

        var pick = candidates[_pseudoRandomNumbers.Next(candidates.Count)];
        direction = pick.direction;
        position = pick.position!.Value;
        return true;
    }

    private static void CarvePassage(DraftMaze draft, CellPosition from, CardinalDirection direction, CellPosition to)
    {
        var (fromWall, toWall) = direction switch
        {
            CardinalDirection.North => (Walls.Top, Walls.Bottom),
            CardinalDirection.East => (Walls.Right, Walls.Left),
            CardinalDirection.South => (Walls.Bottom, Walls.Top),
            CardinalDirection.West => (Walls.Left, Walls.Right),
            _ => throw new ArgumentException($"Unexpected direction {direction}", nameof(direction)),
        };

        draft[from] &= ~fromWall;
        draft[to] &= ~toWall;
    }

    private CellPosition RandomInBounds(DraftMaze draft)
    {
        var randomY = _pseudoRandomNumbers.Next(draft.Height);
        var randomX = _pseudoRandomNumbers.Next(draft.Width);

        return new CellPosition(new YCoordinate(randomY), new XCoordinate(randomX));
    }
}