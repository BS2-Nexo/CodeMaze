using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace CodeMaze.MazeLang.Parser.Text;

/// <summary>Thrown when a language document is malformed or describes an invalid language.</summary>
public sealed class LanguageFormatException : Exception
{
    public IReadOnlyList<string> Errors { get; }
    public LanguageFormatException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors)) => Errors = errors;
}

/// <summary>TOML text ⇄ <see cref="LanguageDefinition"/>. Pure string in / string out.</summary>
public static class LanguageToml
{
    private static readonly Dictionary<string, Keyword> KeywordNames =
        Enum.GetValues<Keyword>().ToDictionary(k => k.ToString(), k => k, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TopLevelKeys =
        new(["name", "display_name", "comment_prefix", "keywords", "replacements"], StringComparer.Ordinal);


    /// <param name="source">Label for error messages (file name, URL, ...).</param>
    public static LanguageDefinition Parse(string toml, string source = "<toml>")
    {
        ArgumentNullException.ThrowIfNull(toml);

        TomlTable? root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(toml);
        }
        catch (TomlException e)
        {
            throw new LanguageFormatException([$"{source}: {e.Message}"]);
        }
        if (root is null)
            throw new LanguageFormatException([$"{source}: The document is empty."]);

        var errors = new List<string>();
        void Error(string msg) => errors.Add($"{source}: {msg}");

        foreach (var key in root.Keys)
            if (!TopLevelKeys.Contains(key))
                Error($"Unknown key '{key}'. Expected: {string.Join(", ", TopLevelKeys)}.");

        string? ReadString(TomlTable table, string key)
        {
            if (!table.TryGetValue(key, out var v)) return null;
            if (v is string s) return s;
            Error($"'{key}' must be a string.");
            return null;
        }

        var name = ReadString(root, "name");
        if (string.IsNullOrWhiteSpace(name)) Error("'name' is required.");
        var displayName = ReadString(root, "display_name");
        var comment = ReadString(root, "comment_prefix") ?? "#";

        // [keywords]
        var keywords = new Dictionary<Keyword, string[]>();
        if (root.TryGetValue("keywords", out var kwObj) && kwObj is TomlTable kwTable)
        {
            foreach (var (key, value) in kwTable)
            {
                if (!KeywordNames.TryGetValue(key, out var kw))
                {
                    Error($"Unknown keyword '{key}'. Valid: {string.Join(", ", KeywordNames.Keys)}.");
                    continue;
                }

                string[]? literals = value switch
                {
                    string s => [s],
                    TomlArray arr when arr.All(x => x is string) => arr.Cast<string>().ToArray(),
                    _ => null,
                };
                if (literals is null)
                {
                    Error($"keywords.{key} must be a string or an array of strings.");
                    continue;
                }
                keywords[kw] = literals;
            }
        }
        else
        {
            Error("The [keywords] table is required.");
        }

        // [replacements]
        var replacements = new Dictionary<string, string>();
        if (root.TryGetValue("replacements", out var repObj))
        {
            if (repObj is TomlTable repTable)
            {
                foreach (var (from, to) in repTable)
                {
                    if (to is string s) replacements[from] = s;
                    else Error($"replacements.\"{from}\" must be a string.");
                }
            }
            else
            {
                Error("'replacements' must be a table.");
            }
        }

        if (errors.Count > 0)
            throw new LanguageFormatException(errors);

        var def = new LanguageDefinition
        {
            Name = name!,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            Keywords = keywords,
            CharReplacements = replacements,
            CommentPrefix = comment,
        };

        if (Validate(def) is { } problem)
            throw new LanguageFormatException([$"{source}: {problem}"]);
        return def;
    }

    /// <summary>Template / export. The output can be read again with <see cref="Parse"/>.</summary>
    public static string Write(LanguageDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);
        var sb = new StringBuilder();
        sb.AppendLine($"name = {Quote(def.Name)}");
        if (def.DisplayName is not null) sb.AppendLine($"display_name = {Quote(def.DisplayName)}");
        sb.AppendLine($"comment_prefix = {Quote(def.CommentPrefix)}");
        sb.AppendLine();
        sb.AppendLine("[keywords]");
        foreach (var k in Enum.GetValues<Keyword>())
        {
            if (!def.Keywords.TryGetValue(k, out var lits) || lits.Length == 0) continue;
            var value = lits.Length == 1 ? Quote(lits[0]) : "[" + string.Join(", ", lits.Select(Quote)) + "]";
            sb.AppendLine($"{k.ToString().ToLowerInvariant()} = {value}");
        }
        if (def.CharReplacements.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[replacements]");
            foreach (var (from, to) in def.CharReplacements)
                sb.AppendLine($"{Quote(from)} = {Quote(to)}");
        }
        return sb.ToString();
    }

    /// <summary>Returns a description of the first problem, or null if the language is usable.</summary>
    internal static string? Validate(LanguageDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Name) ||
            !def.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return $"'{def.Name}' is not a valid language name (letters, digits, '-' and '_' only).";
        try { _ = new LanguageTable(def); }
        catch (ArgumentException e) { return e.Message; }
        return null;
    }

    /// <summary>TOML basic string. Non-ASCII text stays as is (TOML is UTF-8).</summary>
    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        return sb.Append('"').ToString();
    }
}