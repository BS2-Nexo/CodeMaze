using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeMaze.Server.Services;

public interface IPickupCodeGenerator
{
    string Generate();
}

/// <summary>Generates codes like "fox-lamp-42".</summary>
public sealed class PickupCodeGenerator : IPickupCodeGenerator
{
    // Short, easy to spell, easy to type on a phone.
    private static readonly string[] Words =
    [
        "fuchs","lampe","eule","kiefer","mond","stern","riff","düne","farn","drachen","blitz","klaue","taube","elch",
        "frosch","edelstein","falke","tinte","jade","karpfen","lerche","minze","nebel","opal","perle","wachtel","rabe","salbei",
        "tiger","rebe","wolf","yak","zink","bernstein","birke","zeder","delta","glut","feuerstein","hain","dunst",
        "iris","gelee","knoten","lotos","ahorn","edel","olive","panda","rotkehlchen","sturm","tulpe","ocker",
        "viper","weide","yeti","zebra","eichel","dachs","kobra","drift","adler","falke","hafen",
        "insel","jaguar","tang","zitrone","mango","otter","pfeffer","federkiel","rakete","funke","donner"
    ];

    // ~77 words * 76 * 90 numbers ~ 527k combos. Fine because guesses are rate-limited per name.
    public string Generate()
    {
        var first = RandomNumberGenerator.GetInt32(Words.Length);
        int second;
        do { second = RandomNumberGenerator.GetInt32(Words.Length); } while (second == first);
        var number = RandomNumberGenerator.GetInt32(10, 100);
        return $"{Words[first]}-{Words[second]}-{number}";
    }
}

/// <summary>Keyed HMAC so a leaked DB can't be brute-forced offline without the server key.</summary>
public sealed class CodeHasher
{
    private readonly byte[] _key;

    public CodeHasher(IConfiguration config)
    {
        var key = config["CodeMaze:CodeKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("CodeMaze:CodeKey must be set (32+ chars).");
        _key = Encoding.UTF8.GetBytes(key);
    }

    // Forgiving input: "Fox Lamp 42", "fox_lamp_42" and "fox-lamp-42" all match.
    public static string Normalize(string code) =>
        Regex.Replace(code.Trim().ToLowerInvariant(), @"[\s_\-]+", "-");

    public byte[] Hash(string code) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(Normalize(code)));

    public bool Matches(string code, byte[] stored) =>
        CryptographicOperations.FixedTimeEquals(Hash(code), stored);
}