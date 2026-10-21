using CodeMaze.MazeGeneration;
using CodeMaze.MazeLang.Core;
namespace CodeMaze.MazeLang.Interpreter;
/// <summary>Mutable run state: position, visited cells and move history.</summary>
public sealed class MazeState
{
    private readonly IMazeLayout _layout;
    private readonly HashSet<CellPosition> _visited = new();
    private readonly Stack<CellPosition> _history = new();

    public MazeState(IMazeLayout layout)
    {
        _layout = layout;
        Position = layout.Entrance;
        _visited.Add(Position);
    }

    public CellPosition Position { get; private set; }
    public CellPosition Exit => _layout.Exit;
    public int Steps { get; private set; }
    public bool AtExit => Position == Exit;

    private CellPosition Neighbor(Direction d)
    {
        var (dy, dx) = d.Delta();
        return new CellPosition(Position.Y + dy, Position.X + dx);
    }

    private bool TryTarget(Direction d, out CellPosition target)
    {
        target = Neighbor(d);
        return !_layout.HasWall(Position, d) && _layout.Contains(target);
    }

    public bool IsFree(Direction d) => TryTarget(d, out _);

    public bool IsVisited(Direction d)
    {
        var n = Neighbor(d);
        return _layout.Contains(n) && _visited.Contains(n);
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