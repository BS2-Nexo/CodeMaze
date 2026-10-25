using CodeMaze.MazeGeneration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodeMaze.MazeGeneration.Abstractions;

public readonly record struct SizeRange(int Min, int Max)
{
    public bool Contains(int value) => value >= Min && value <= Max;
}

public record GeneratorMapping(
    SizeRange Height,
    SizeRange Width,
    Difficulty Difficulty,
    IGeneratorAlgorithm Algorithm)
{
    public bool Matches(int height, int width) =>
        Height.Contains(height) && Width.Contains(width);
}

public interface IGeneratorMappingStore
{
    IReadOnlyList<GeneratorMapping> GetAll();
}

public class NoGeneratorFoundException : Exception
{
    public NoGeneratorFoundException(string message) : base(message) { }
}

public class MazeGenerationOptions
{
    public const string SectionName = "MazeGeneration";
    public List<GeneratorMappingOptions> Mappings { get; set; } = new();
}

public class GeneratorMappingOptions
{
    public SizeRangeOptions? Size { get; set; }
    public SizeRangeOptions? Height { get; set; }
    public SizeRangeOptions? Width { get; set; }
    public Difficulty Difficulty { get; set; }
    public string Algorithm { get; set; } = string.Empty;
}

public class SizeRangeOptions
{
    public int Min { get; set; }
    public int Max { get; set; }
}

public class ConfigGeneratorMappingStore : IGeneratorMappingStore
{
    private readonly IReadOnlyList<GeneratorMapping> _mappings;

    public ConfigGeneratorMappingStore(
        IOptions<MazeGenerationOptions> options,
        IServiceProvider services)
    {
        _mappings = options.Value.Mappings
            .Select((m, i) => ToMapping(m, i, services))
            .ToList();

        if (_mappings.Count == 0)
            throw new InvalidOperationException(
                $"No mappings configured under '{MazeGenerationOptions.SectionName}:Mappings'.");
    }

    public IReadOnlyList<GeneratorMapping> GetAll() => _mappings;

    private static GeneratorMapping ToMapping(
        GeneratorMappingOptions o, int index, IServiceProvider services)
    {
        var where = $"{MazeGenerationOptions.SectionName}:Mappings:{index}";

        var height = ToRange(o.Height ?? o.Size, $"{where}:Height (or Size)");
        var width = ToRange(o.Width ?? o.Size, $"{where}:Width (or Size)");

        if (!Enum.IsDefined(o.Difficulty))
            throw new InvalidOperationException($"{where}:Difficulty is invalid.");

        var algorithm = services.GetKeyedService<IGeneratorAlgorithm>(o.Algorithm)
            ?? throw new InvalidOperationException(
                $"{where}:Algorithm '{o.Algorithm}' is not a registered algorithm.");

        return new GeneratorMapping(height, width, o.Difficulty, algorithm);
    }

    private static SizeRange ToRange(SizeRangeOptions? r, string path)
    {
        if (r is null)
            throw new InvalidOperationException($"{path} is missing.");
        if (r.Min < 1 || r.Min > r.Max)
            throw new InvalidOperationException(
                $"{path} is invalid (Min={r.Min}, Max={r.Max}).");
        return new SizeRange(r.Min, r.Max);
    }
}