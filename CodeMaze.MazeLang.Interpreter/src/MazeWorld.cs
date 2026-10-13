using System.Text.Json;
using CodeMaze.MazeLang.Core;

namespace CodeMaze.MazeLang.Interpreter;

public readonly record struct CellPosition(int Y, int X);

/// <summary>Read-only description of a maze. Implement this to plug in your own maze model.</summary>
public interface IMazeLayout
{
    CellPosition Entrance { get; }
    CellPosition Exit { get; }
    /// <summary>True if the cell exists (is part of the maze).</summary>
    bool Contains(CellPosition cell);
    /// <summary>True if there is a wall on the given side (Up/Down/Left/Right) of the cell.</summary>
    bool HasWall(CellPosition cell, Direction side);
}

public static class DirectionExtensions
{
    /// <summary>
    /// The only place that knows the concrete Direction members. ADJUST the member names
    /// here if your enum differs (hoch/runter/links/rechts = Up/Down/Left/Right assumed).
    /// </summary>
    public static (int Dy, int Dx) Delta(this Direction d) => d switch
    {
        Direction.Up => (-1, 0),
        Direction.Down => (1, 0),
        Direction.Left => (0, -1),
        Direction.Right => (0, 1),
        _ => throw new InvalidOperationException($"'{d}' is not a physical direction."),
    };
}

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

/// <summary>Loads the maze JSON format used by the prototype (entrance, exit, width, height, cells[{y,x,walls}]).</summary>
public sealed class JsonMazeLayout : IMazeLayout
{
    private readonly Dictionary<CellPosition, HashSet<string>> _cells = new();

    public CellPosition Entrance { get; }
    public CellPosition Exit { get; }
    public int Width { get; }
    public int Height { get; }

    private JsonMazeLayout(JsonElement root)
    {
        Width = root.GetProperty("width").GetInt32();
        Height = root.GetProperty("height").GetInt32();
        Entrance = Pos(root.GetProperty("entrance"));
        Exit = Pos(root.GetProperty("exit"));
        foreach (var c in root.GetProperty("cells").EnumerateArray())
        {
            var walls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in c.GetProperty("walls").EnumerateArray())
                walls.Add(w.GetString()!);
            _cells[Pos(c)] = walls;
        }
    }

    private static CellPosition Pos(JsonElement e) => new(e.GetProperty("y").GetInt32(), e.GetProperty("x").GetInt32());

    public static JsonMazeLayout Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new JsonMazeLayout(doc.RootElement);
    }

    public static JsonMazeLayout Load(string path) => Parse(File.ReadAllText(path));

    public bool Contains(CellPosition cell) => _cells.ContainsKey(cell);

    public bool HasWall(CellPosition cell, Direction side)
    {
        if (!_cells.TryGetValue(cell, out var walls)) return true;
        string name = side switch
        {
            Direction.Up => "top",
            Direction.Down => "bottom",
            Direction.Left => "left",
            Direction.Right => "right",
            _ => throw new InvalidOperationException($"'{side}' is not a physical direction."),
        };
        return walls.Contains(name);
    }
}