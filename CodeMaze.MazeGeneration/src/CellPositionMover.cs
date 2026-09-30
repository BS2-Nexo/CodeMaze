namespace CodeMaze.MazeGeneration;

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
                $"Unexpected Direction {direction} expected {CardinalDirection.North}, {CardinalDirection.East}, {CardinalDirection.South} or {CardinalDirection.West}",
                nameof(direction)),
        };

        _boundsCheck.Invoke(nextPos);

        return nextPos;
    }

    public Neighbors GetNeighborsOf(CellPosition current)
    {
        CellPosition?[] neighbors = new CellPosition?[Enum.GetValues<CardinalDirection>().Count()];
        foreach (var direction in Enum.GetValues<CardinalDirection>())
        {
            try
            {
                var nextPos = Move(direction, current);
                neighbors[(int)direction] = nextPos;
            }
            catch (ArgumentException)
            {
                neighbors[(int)direction] = null;
                continue;
            }
            catch
            {
                throw;
            }
        }
        return new Neighbors(neighbors[(int)CardinalDirection.North],
                             neighbors[(int)CardinalDirection.East],
                             neighbors[(int)CardinalDirection.South],
                             neighbors[(int)CardinalDirection.West]);
    }
}