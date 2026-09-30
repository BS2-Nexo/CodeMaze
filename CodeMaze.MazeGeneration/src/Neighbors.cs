namespace CodeMaze.MazeGeneration;

public readonly record struct Neighbors(
    CellPosition? North,
    CellPosition? East,
    CellPosition? South,
    CellPosition? West
)
{
    public IEnumerable<(CardinalDirection direction, CellPosition? position)> Valid
    {
        get
        {
            if (North is not null) yield return (CardinalDirection.North, North);
            if (East is not null) yield return (CardinalDirection.East, East);
            if (South is not null) yield return (CardinalDirection.South, South);
            if (West is not null) yield return (CardinalDirection.West, West);
        }
    }
}