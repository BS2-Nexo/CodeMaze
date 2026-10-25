using CodeMaze.MazeGeneration.Abstractions;

namespace CodeMaze.MazeGeneration.Generator;

public class MazeGenerator : IMazeGenerator
{
    private readonly IGeneratorMappingStore _store;
    private readonly Random _random;

    public MazeGenerator(IGeneratorMappingStore store, Random? random = null)
    {
        _store = store;
        _random = random ?? Random.Shared;
    }

    public DraftMaze Generate(int height, int width)
    {
        var candidates = _store.GetAll()
            .Where(m => m.Matches(height, width))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new NoGeneratorFoundException(
                $"No generator algorithm is configured for size {height}x{width}.");
        }

        return Run(PickRandom(candidates), height, width);
    }

    public DraftMaze Generate(int height, int width, Difficulty difficulty)
    {
        var candidates = _store.GetAll()
            .Where(m => m.Matches(height, width) && m.Difficulty == difficulty)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new NoGeneratorFoundException(
                $"No generator algorithm is configured for size {height}x{width} " +
                $"with difficulty {difficulty}.");
        }

        return Run(PickRandom(candidates), height, width);
    }

    private GeneratorMapping PickRandom(IReadOnlyList<GeneratorMapping> candidates) =>
        candidates[_random.Next(candidates.Count)];

    private static DraftMaze Run(GeneratorMapping mapping, int height, int width)
    {
        var draft = mapping.Algorithm.Generate(height, width);
        return draft;
    }
}