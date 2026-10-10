using System.Text.RegularExpressions;

namespace Dictate.Core;

public sealed record DictionaryEntry(string Heard, string Written);
internal sealed record Replacement(int Start, int Length, string Written);

public sealed class PersonalDictionary
{
    private readonly Dictionary<string, string> replacements = new();
    private readonly Regex? pattern;

    public PersonalDictionary(IReadOnlyList<DictionaryEntry> entries)
    {
        foreach (var entry in entries)
        {
            var key = Normalize(entry.Heard);
            var value = entry.Written.Trim();
            if (key.Length > 0 && value.Length > 0) replacements[key] = value;
        }
        if (replacements.Count == 0) return;
        var alternatives = replacements.Keys.OrderByDescending(key => key.Length)
            .Select(key => string.Join(@"[\s,]+", key.Split(' ').Select(Regex.Escape)));
        pattern = new Regex(@"(?<![\p{L}\p{N}'’])(?:" + string.Join('|', alternatives) + @")(?![\p{L}\p{N}'’])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string Normalize(string value) => string.Join(' ', Regex.Split(value.ToLowerInvariant().Replace(',', ' ').Trim(), @"\s+").Where(word => word.Length > 0));

    internal IEnumerable<Replacement> Replacements(string text) => pattern is null ? [] :
        pattern.Matches(text).Select(match => new Replacement(match.Index, match.Length, replacements[Normalize(match.Value)]));

    public string Apply(string text)
    {
        foreach (var match in Replacements(text).Reverse())
        {
            var replacement = match.Written;
            var firstWord = replacement.TakeWhile(c => !char.IsWhiteSpace(c));
            if (char.IsUpper(text[match.Start]) && !firstWord.Any(char.IsUpper)) replacement = TextCleanup.Capitalize(replacement);
            text = text[..match.Start] + replacement + text[(match.Start + match.Length)..];
        }
        return text;
    }
}
