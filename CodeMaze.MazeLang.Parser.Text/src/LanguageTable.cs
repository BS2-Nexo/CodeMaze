namespace CodeMaze.MazeLang.Parser.Text;

/// <summary>Validated, pre-normalized form of a <see cref="LanguageDefinition"/>.</summary>
internal sealed class LanguageTable
{
    // Optional: "[mal]" can be omitted, and the DSL has no boolean literals yet.
    private static readonly HashSet<Keyword> Optional = [Keyword.Times, Keyword.True, Keyword.False];

    private readonly (string From, string To)[] _replacements;
    private readonly Dictionary<string, Keyword> _lookup = new(StringComparer.Ordinal);
    private readonly Dictionary<Keyword, string> _display = new();

    public string CommentPrefix { get; }

    public LanguageTable(LanguageDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);
        CommentPrefix = def.CommentPrefix ?? "";

        // longest 'from' first so multi-char rules win deterministically
        _replacements = def.CharReplacements
            .Select(kv => (From: kv.Key.ToLowerInvariant(), To: kv.Value.ToLowerInvariant()))
            .Where(r => r.From.Length > 0)
            .OrderByDescending(r => r.From.Length)
            .ToArray();

        foreach (var kw in Enum.GetValues<Keyword>())
        {
            def.Keywords.TryGetValue(kw, out var literals);
            if (literals is null || literals.Length == 0)
            {
                if (Optional.Contains(kw)) continue;
                throw new ArgumentException($"The language definition has no literal for '{kw}'.");
            }
            _display[kw] = literals[0];
            foreach (var literal in literals)
            {
                var norm = Normalize(literal);
                if (norm.Length == 0 || !norm.All(c => char.IsLetterOrDigit(c) || c == '_') || char.IsDigit(norm[0]))
                    throw new ArgumentException($"'{literal}' ({kw}) is not a valid single-word literal.");
                if (_lookup.TryGetValue(norm, out var other) && other != kw)
                    throw new ArgumentException($"'{literal}' is used for both {other} and {kw}.");
                _lookup[norm] = kw;
            }
        }
    }

    public string Normalize(string word)
    {
        var s = word.ToLowerInvariant();
        foreach (var (from, to) in _replacements)
            s = s.Replace(from, to, StringComparison.Ordinal);
        return s;
    }

    public Keyword? TryKeyword(string normalized) =>
        _lookup.TryGetValue(normalized, out var k) ? k : null;

    /// <summary>First literal of a keyword, for error messages.</summary>
    public string Display(Keyword k) => _display.TryGetValue(k, out var d) ? d : k.ToString();
}