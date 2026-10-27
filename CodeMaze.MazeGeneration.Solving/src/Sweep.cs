using CodeMaze.MazeGeneration.Abstractions;
using CodeMaze.MazeGeneration.Generator;

namespace CodeMaze.MazeGeneration.Solving;

public sealed record MazeRun(
    int Height,
    int Width,
    Difficulty Difficulty,
    Maze Maze,
    IReadOnlyList<CellPosition>? Path)
{
    public int CellCount => Height * Width;
    public bool IsSolvable => Path is not null;

    /// <summary>Number of moves (cells visited minus one). -1 if unsolvable.</summary>
    public int Steps => Path is null ? -1 : Path.Count - 1;

    /// <summary>True if the solution takes at most as many steps as there are cells.</summary>
    public bool WithinCellCount => Path is not null && Steps <= CellCount;
}

public static class MazeSweep
{
    public static IEnumerable<MazeRun> Run(
        IMazeGenerator generator,
        int startSize = 5,
        int maxSize = 50,
        int sizeStep = 5,
        bool square = true)
    {
        ArgumentNullException.ThrowIfNull(generator);

        foreach (var difficulty in Enum.GetValues<Difficulty>())
        {
            for (var size = startSize; size <= maxSize; size += sizeStep)
            {
                var height = size;
                var width = square ? size : size + sizeStep;

                DraftMaze draft;
                try
                {
                    draft = generator.Generate(height, width, difficulty);
                }
                catch (NoGeneratorFoundException)
                {
                    continue; // nothing configured for this size/difficulty
                }

                var maze = draft.Finalize();
                yield return new MazeRun(height, width, difficulty, maze, MazeSolver.FindShortestPath(maze));
            }
        }
    }
}