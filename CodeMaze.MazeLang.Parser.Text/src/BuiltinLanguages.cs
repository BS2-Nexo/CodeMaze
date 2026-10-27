namespace CodeMaze.MazeLang.Parser.Text;

/// <summary>The languages that ship with the library and are always available.</summary>
public static class BuiltinLanguages
{
    public static LanguageDefinition German { get; } = new()
    {
        Name = "de",
        DisplayName = "Deutsch",
        Keywords = new Dictionary<Keyword, string[]>
        {
            [Keyword.Go] = ["gehe"],
            [Keyword.Repeat] = ["wiederhole"],
            [Keyword.Times] = ["mal"],
            [Keyword.End] = ["ende"],
            [Keyword.Forever] = ["immer"],
            [Keyword.If] = ["wenn"],
            [Keyword.Else] = ["sonst"],
            [Keyword.Up] = ["hoch"],
            [Keyword.Down] = ["runter"],
            [Keyword.Left] = ["links"],
            [Keyword.Right] = ["rechts"],
            [Keyword.Back] = ["rückwärts", "zurück"],
            [Keyword.Free] = ["frei"],
            [Keyword.Visited] = ["besucht"],
            [Keyword.Not] = ["nicht"],
            [Keyword.And] = ["und"],
            [Keyword.Or] = ["oder"],
            [Keyword.Function] = ["funktion"],
            [Keyword.Result] = ["ergebnis"],
        },
        CharReplacements = new Dictionary<string, string>
        {
            ["ä"] = "ae",
            ["ö"] = "oe",
            ["ü"] = "ue",
            ["ß"] = "ss",
        },
    };

    public static LanguageDefinition English { get; } = new()
    {
        Name = "en",
        DisplayName = "English",
        Keywords = new Dictionary<Keyword, string[]>
        {
            [Keyword.Go] = ["go"],
            [Keyword.Repeat] = ["repeat"],
            [Keyword.Times] = ["times"],
            [Keyword.End] = ["end"],
            [Keyword.Forever] = ["forever"],
            [Keyword.If] = ["if"],
            [Keyword.Else] = ["else"],
            [Keyword.Up] = ["up"],
            [Keyword.Down] = ["down"],
            [Keyword.Left] = ["left"],
            [Keyword.Right] = ["right"],
            [Keyword.Back] = ["back", "backwards"],
            [Keyword.Free] = ["free"],
            [Keyword.Visited] = ["visited"],
            [Keyword.Not] = ["not"],
            [Keyword.And] = ["and"],
            [Keyword.Or] = ["or"],
            [Keyword.Function] = ["function"],
            [Keyword.Result] = ["result"],
        },
    };

    public static IReadOnlyList<LanguageDefinition> All { get; } = [German, English];
}