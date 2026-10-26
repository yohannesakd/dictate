using System.Globalization;
using System.Text.RegularExpressions;

namespace Dictate.Core;

public sealed record CleanupRules(bool Fillers = true, bool Repeats = true, bool ScratchThat = true);

public static class TextCleanup
{
    private static readonly HashSet<string> Fillers = ["um", "umm", "uh", "uhh", "uhm", "erm", "er", "ah", "ahh", "hm", "hmm", "mm", "mmm"];
    private static readonly HashSet<string> Stutters = ["i", "a", "an", "the", "to", "and", "but", "or", "so", "of", "in", "on", "at", "for", "with", "is", "it", "we", "you", "he", "she", "they", "my", "this", "can", "will", "would", "just", "if", "be", "was", "are", "have"];
    private static readonly Regex Scratch = new(@"(?:^|[,;.!?]\s+)scratch that(?:[,;.!?](?:\s+|$)|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Clean(string text, CleanupRules rules)
    {
        var value = rules.ScratchThat ? ApplyScratchThat(text) : text;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (rules.Fillers) words = RemoveFillers(words);
        if (rules.Repeats) words = RemoveStutters(words);
        var result = string.Join(' ', words);
        return result == text ? text : Capitalize(result);
    }

    internal static string Capitalize(string value)
    {
        if (value.Length == 0) return value;
        var first = StringInfo.GetNextTextElement(value);
        return first.ToUpperInvariant() + value[first.Length..];
    }

    private static string TrimPunctuation(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && char.IsPunctuation(value[start])) start++;
        while (end > start && char.IsPunctuation(value[end - 1])) end--;
        return value[start..end];
    }

    private static string Core(string value) => TrimPunctuation(value).ToLowerInvariant();
    private static bool EndsSentence(string value) => value.Length > 0 && ".!?".Contains(value[^1]);

    private static string ApplyScratchThat(string value)
    {
        for (var match = Scratch.Match(value); match.Success; match = Scratch.Match(value))
        {
            var before = value[..match.Index];
            var keep = before.LastIndexOfAny(['.', '!', '?']) + 1;
            value = (before[..keep] + " " + Capitalize(value[(match.Index + match.Length)..])).Trim();
        }
        return value;
    }

    private static List<string> RemoveFillers(List<string> words)
    {
        var output = new List<string>();
        var capitalize = false;
        foreach (var word in words)
        {
            if (!Fillers.Contains(Core(word)) || TrimPunctuation(word) == "ER")
            {
                output.Add(capitalize ? Capitalize(word) : word);
                capitalize = false;
                continue;
            }
            if (output.Count == 0) { capitalize = true; continue; }
            var previous = output[^1];
            if (EndsSentence(previous)) capitalize = true;
            else if (EndsSentence(word)) previous = previous.Trim(',', ';') + word[^1];
            else if (word.EndsWith(',') && previous.EndsWith(',')) previous = previous[..^1];
            output[^1] = previous;
        }
        return output;
    }

    private static List<string> RemoveStutters(List<string> words)
    {
        var output = new List<string>();
        foreach (var word in words)
        {
            if (output.Count > 0 && Core(output[^1]) == Core(word) && Stutters.Contains(Core(word))
                && char.IsLetterOrDigit(output[^1][^1]))
                output[^1] = char.IsUpper(output[^1][0]) ? Capitalize(word) : word;
            else output.Add(word);
        }
        return output;
    }
}
