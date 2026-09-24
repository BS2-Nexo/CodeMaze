namespace CodeMaze.MazeGeneration.Maze;

public sealed class CellPositionMover
{
    private readonly CellPosition Y = new(new YCoordinate(1), new XCoordinate(0));
    private readonly CellPosition X = new(new YCoordinate(0), new XCoordinate(1));
    private readonly Action<CellPosition> _boundsCheck;
    public CellPositionMover(Action<CellPosition> BoundsCheck)
    {
        _boundsCheck = BoundsCheck;
    }

    public CellPosition Move(CardinalDirection direction, CellPosition current)
    {
        _boundsCheck.Invoke(current);

        var nextPos = direction switch
        {
            CardinalDirection.North => current - Y,

            CardinalDirection.East => current + X,

            CardinalDirection.South => current + Y,

            CardinalDirection.West => current - X,

            _ => throw new ArgumentException(
                $"Unexpected Direction {direction} expected North, East, South or West",
                nameof(direction)),
        };

        _boundsCheck.Invoke(nextPos);

        return nextPos;
    }
}