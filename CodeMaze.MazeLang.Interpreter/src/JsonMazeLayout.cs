using System.Text.Json;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeGeneration;
namespace CodeMaze.MazeLang.Interpreter;
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

    private static CellPosition Pos(JsonElement e) => new(new YCoordinate(e.GetProperty("y").GetInt32()),
        new XCoordinate(e.GetProperty("x").GetInt32()));

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