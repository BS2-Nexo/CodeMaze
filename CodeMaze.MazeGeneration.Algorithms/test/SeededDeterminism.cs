using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Abstractions;
using Xunit;

namespace CodeMaze.AlgorithmTests;

internal static class SeededDeterminism
{
    public static void AssertSameMazeForSameSeed(
        Func<Random, IGeneratorAlgorithm> createAlgorithm,
        int seed,
        int height,
        int width)
    {
        var first = createAlgorithm(new Random(seed)).Generate(height, width, default);
        var second = createAlgorithm(new Random(seed)).Generate(height, width, default);

        Assert.Equal(height, first.Height);
        Assert.Equal(width, first.Width);
        Assert.Equal(InternalLayout(first), InternalLayout(second));
    }

    // Walls between two cells only, in row-major order. Each internal wall is read once from the
    // cell that owns it (its right and bottom wall). The outer boundary, where the entrance and
    // exit live, is deliberately not part of the comparison.
    private static bool[] InternalLayout(DraftMaze draft)
    {
        var walls = new List<bool>();

        for (var y = 0; y < draft.Height; y++)
        {
            for (var x = 0; x < draft.Width; x++)
            {
                var cell = draft[new CellPosition(new YCoordinate(y), new XCoordinate(x))];

                if (x < draft.Width - 1)
                {
                    walls.Add(cell.HasFlag(Walls.Right));
                }

                if (y < draft.Height - 1)
                {
                    walls.Add(cell.HasFlag(Walls.Bottom));
                }
            }
        }

        return walls.ToArray();
    }
}