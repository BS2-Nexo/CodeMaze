using CodeMaze.MazeGeneration;
using CodeMaze.MazeLang.Core;

namespace CodeMaze.MazeLang.Interpreter;

/// <summary>Mutable run state: position, visited cells and move history.</summary>
public sealed class MazeState
{
    private readonly Maze _maze;
    private readonly HashSet<CellPosition> _visited = new();
    private readonly Stack<CellPosition> _history = new();

    public MazeState(Maze maze)
    {
        ArgumentNullException.ThrowIfNull(maze);
        _maze = maze;
        Position = maze.Entrance;
        _visited.Add(Position);
    }

    public CellPosition Position { get; private set; }
    public CellPosition Exit => _maze.Exit;
    public int Steps { get; private set; }
    public bool AtExit => Position == Exit;

    private CellPosition Neighbor(Direction d)
    {
        var (dy, dx) = d.Delta();
        return new CellPosition(Position.Y + dy, Position.X + dx);
    }

    /// <summary>True if there is a wall on the given side of the current cell.</summary>
    private bool HasWall(Direction side) => (_maze[Position] & ToWall(side)) != Walls.None;

    private static Walls ToWall(Direction side) => side switch
    {
        Direction.Up => Walls.Top,
        Direction.Down => Walls.Bottom,
        Direction.Left => Walls.Left,
        Direction.Right => Walls.Right,
        _ => throw new InvalidOperationException($"'{side}' is not a physical direction."),
    };

    private bool TryTarget(Direction d, out CellPosition target)
    {
        target = Neighbor(d);
        return !HasWall(d) && _maze.Contains(target);
    }

    public bool IsFree(Direction d) => TryTarget(d, out _);

    public bool IsVisited(Direction d)
    {
        var n = Neighbor(d);
        return _maze.Contains(n) && _visited.Contains(n);
    }

    /// <summary>Moves one cell. Returns false (and changes nothing) if blocked.</summary>
    public bool TryMove(Direction d, out CellPosition from, out CellPosition to)
    {
        from = Position;
        if (d == Direction.Back)
        {
            if (_history.Count == 0) { to = from; return false; }
            to = _history.Pop();
        }
        else
        {
            if (!TryTarget(d, out to)) return false;
            _history.Push(from);
        }
        Position = to;
        _visited.Add(to);
        Steps++;
        return true;
    }
}