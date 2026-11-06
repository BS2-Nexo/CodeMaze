using Prims = CodeMaze.MazeGeneration.Algorithms.Prims.Prims;
using Xunit;

namespace CodeMaze.AlgorithmTests;

public class PrimsTests
{
    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(42, 8, 15)]
    [InlineData(2024, 20, 5)]
    public void SameSeed_ProducesSameMaze(int seed, int height, int width) =>
        SeededDeterminism.AssertSameMazeForSameSeed(rng => new Prims(rng), seed, height, width);
}