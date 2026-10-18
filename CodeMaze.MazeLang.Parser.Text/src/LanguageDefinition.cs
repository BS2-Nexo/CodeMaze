namespace CodeMaze.MazeLang.Parser.Text;

public sealed class LanguageDefinition
{
    /// <summary>Identifier, e.g. "de", "fr". Letters, digits, '-' and '_'; compared case-insensitively.</summary>
    public required string Name { get; init; }
    /// <summary>Human readable name for UIs, e.g. "Français". Falls back to <see cref="Name"/>.</summary>
    public string? DisplayName { get; init; }

    public required IReadOnlyDictionary<Keyword, string[]> Keywords { get; init; }
    public IReadOnlyDictionary<string, string> CharReplacements { get; init; } = new Dictionary<string, string>();
    /// <summary>Empty string disables comments.</summary>
    public string CommentPrefix { get; init; } = "#";
}