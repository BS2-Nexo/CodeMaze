using System.Text.Json.Serialization;
using CodeMaze.MazeGeneration.Serialization;

namespace CodeMaze.MazeGeneration;

/// <summary>
/// Immutable, finished maze. Cells are stored row-major in a flat array
/// (index = y * Width + x) for O(1), allocation-free lookups.
/// </summary>
[JsonConverter(typeof(MazeSerializer))]
public sealed class Maze
{
    private readonly Walls[] _cells;

    public int Width { get; }
    public int Height { get; }
    public CellPosition Entrance { get; }
    public CellPosition Exit { get; }

    internal Maze(int height, int width, Walls[] cells, CellPosition entrance, CellPosition exit)
    {
        Height = height;
        Width = width;
        _cells = cells;
        Entrance = entrance;
        Exit = exit;
    }

    internal Maze(DraftMaze draft)
    {
        Height = draft.Height;
        Width = draft.Width;
        Entrance = draft.Entrance;
        Exit = draft.Exit;
        _cells = draft.GetCells();
    }

    /// <summary>Read-only view of all cells in row-major order.</summary>
    public ReadOnlySpan<Walls> Cells => _cells;

    public Walls this[CellPosition position]
    {
        get
        {
            if (!Contains(position))
            {
                throw new ArgumentOutOfRangeException(nameof(position),
                    $"Position {position} is outside the maze.");
            }

            return _cells[position.Y.Value * Width + position.X.Value];
        }
    }

    public bool Contains(CellPosition position)
    {
        return position.X.Value >= 0
            && position.X.Value < Width
            && position.Y.Value >= 0
            && position.Y.Value < Height;
    }
}