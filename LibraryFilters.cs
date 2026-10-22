using System.Text.RegularExpressions;

namespace RetroGamingCollection;

public sealed record LanguageChoice(string Code, string Label);

public static class LibraryFilters
{
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["En"] = "English", ["Ja"] = "Japanese", ["Fr"] = "French", ["De"] = "German",
        ["Es"] = "Spanish", ["It"] = "Italian", ["Nl"] = "Dutch", ["Pt"] = "Portuguese",
        ["Sv"] = "Swedish", ["No"] = "Norwegian", ["Da"] = "Danish", ["Fi"] = "Finnish",
        ["Ko"] = "Korean", ["Zh"] = "Chinese", ["Ru"] = "Russian", ["Pl"] = "Polish",
        ["Cs"] = "Czech", ["Hu"] = "Hungarian", ["Tr"] = "Turkish", ["Ar"] = "Arabic"
    };
    private static readonly Regex Tags = new(@"\(([^()]*)\)|\[([^\[\]]*)\]", RegexOptions.Compiled);

    private static IEnumerable<string[]> FilenameTags(string title) => Tags.Matches(title)
        .Select(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    public static IReadOnlyList<string> Languages(string title)
    {
        string? Code(string token) => LanguageNames.Keys.FirstOrDefault(k => k.Equals(token, StringComparison.OrdinalIgnoreCase))
            ?? LanguageNames.FirstOrDefault(p => p.Value.Equals(token, StringComparison.OrdinalIgnoreCase)).Key;
        // Only whole language-tag groups count: USA, NP, Unl, revisions and title words are not languages.
        return FilenameTags(title).Select(tokens => tokens.Select(Code).ToArray())
            .Where(tokens => tokens.Length > 0 && tokens.All(t => t is not null))
            .SelectMany(tokens => tokens.Select(t => t!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsUsaRelease(string title) => FilenameTags(title)
        .Any(tokens => tokens.Any(t => t.Equals("USA", StringComparison.OrdinalIgnoreCase)));

    public static bool Matches(GameEntry game, string language, bool usaOnly)
    {
        if (usaOnly && !IsUsaRelease(game.Title)) return false;
        var languages = Languages(game.Title);
        return language == "All" || (language == "Untagged" ? languages.Count == 0 : languages.Contains(language, StringComparer.OrdinalIgnoreCase));
    }

    public static List<LanguageChoice> Choices(IEnumerable<GameEntry> games) =>
        [new("Untagged", "No language tag"), new("All", "All languages"),
         ..games.SelectMany(g => Languages(g.Title)).Distinct(StringComparer.OrdinalIgnoreCase)
             .Select(code => new LanguageChoice(code, LanguageNames[code])).OrderBy(c => c.Label)];
}
