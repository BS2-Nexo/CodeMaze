using System.Diagnostics.CodeAnalysis;
using CodeMaze.MazeLang.Parser.Text;

namespace CodeMaze.Server.Runs;

public sealed record LanguageInfo(string Name, string DisplayName);

/// <summary>
/// The languages this server offers, looked up by name (case-insensitive): the built-in ones
/// (<see cref="BuiltinLanguages"/>, always present) plus any loaded from TOML files.
/// A loaded language can never replace a built-in one or another loaded one.
/// </summary>
public sealed class LanguageRegistry
{
    private const string BuiltIn = "built in";

    private readonly Dictionary<string, LanguageDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<LanguageInfo> _infos;

    /// <param name="additional">Extra languages with a label of where each came from (a file name), for error messages.</param>
    /// <exception cref="InvalidOperationException">An additional language uses a name that is already taken.</exception>
    public LanguageRegistry(IEnumerable<(string Origin, LanguageDefinition Language)>? additional = null)
    {
        var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var language in BuiltinLanguages.All)
        {
            _byName.Add(language.Name, language);
            origins.Add(language.Name, BuiltIn);
        }

        if (additional is not null)
        {
            foreach (var (origin, language) in additional)
            {
                if (!_byName.TryAdd(language.Name, language))
                {
                    var taken = origins[language.Name];
                    throw new InvalidOperationException(taken == BuiltIn
                        ? $"{origin}: the language '{language.Name}' is built in and cannot be replaced."
                        : $"{origin}: the language '{language.Name}' is already defined in {taken}.");
                }

                origins.Add(language.Name, origin);
            }
        }

        _infos = _byName.Values
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new LanguageInfo(l.Name, l.DisplayName ?? l.Name))
            .ToList();
    }

    public IReadOnlyList<LanguageInfo> List() => _infos;

    public bool TryGet(string name, [NotNullWhen(true)] out LanguageDefinition? language) =>
        _byName.TryGetValue(name.Trim(), out language);

    /// <summary>
    /// Built-in languages plus every *.toml file in the directory. A missing directory just means
    /// "built-ins only". Fails fast on an invalid file or a name that is already taken.
    /// </summary>
    public static LanguageRegistry LoadFromDirectory(string directory)
    {
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.toml").Order().ToList()
            : [];

        var additional = files.Select(file =>
        {
            var name = Path.GetFileName(file);
            return (Origin: name, Language: LanguageToml.Parse(File.ReadAllText(file), name));
        });

        return new LanguageRegistry(additional);
    }
}