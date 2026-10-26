using System.Text.RegularExpressions;

namespace Dictate.Core;

public static class CleanupGuard
{
    private static readonly Regex TokenPattern = new(@"[+−-]?(?:\d+(?:[.,]\d+)*|[.,]\d+)%?|[\p{L}\p{N}]+(?:['’][\p{L}]+)*|[+−=<>/$€£%\-*^&|×÷]");
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}]+");
    private const string Separators = ",.;:!?…—–-";

    public static bool NeedsModel(string raw, IReadOnlyList<DictionaryEntry> entries)
    {
        var padded = " " + string.Join(' ', Words(raw)) + " ";
        return entries.Where(entry => Words(entry.Written).Length > 0)
            .Select(entry => string.Join(' ', Words(entry.Heard))).Any(heard => heard.Length > 0 && padded.Contains(" " + heard + " "))
            || Restarts(TextCleanup.Clean(raw, new())).Count > 0;
    }

    public static bool Accepts(string reply, string raw, IReadOnlyList<DictionaryEntry> entries)
    {
        var baseline = JoinWords(TextCleanup.Clean(raw, new()));
        var ranges = TokenPattern.Matches(baseline).ToArray();
        var source = Tokens(baseline);
        var output = Tokens(JoinWords(reply));
        if (output.Length == 0) return false;
        var terms = new Dictionary<int, (int Count, string[] Written)>();
        var cursor = 0;
        foreach (var replacement in new PersonalDictionary(entries).Replacements(baseline))
        {
            var end = replacement.Start + replacement.Length;
            while (cursor < ranges.Length && ranges[cursor].Index + ranges[cursor].Length <= replacement.Start) cursor++;
            var start = cursor;
            while (cursor < ranges.Length && ranges[cursor].Index < end) cursor++;
            if (cursor == start) continue;
            var lower = Math.Min(ranges[start].Index, replacement.Start);
            var upper = Math.Max(ranges[cursor - 1].Index + ranges[cursor - 1].Length, end);
            var fragment = baseline[lower..replacement.Start] + replacement.Written + baseline[end..upper];
            var written = Tokens(fragment);
            if (written.Length > 0) terms[start] = (cursor - start, written);
        }
        var growth = terms.Values.Sum(term => Math.Max(0, term.Written.Length - term.Count));
        if (output.Length > source.Length + growth) return false;
        var abandoned = Restarts(baseline);
        var positions = Enumerable.Range(0, source.Length + 1).Select(_ => new HashSet<int>()).ToArray();
        positions[0].Add(0);
        for (var i = 0; i < source.Length; i++)
        {
            foreach (var j in positions[i])
            {
                if (j < output.Length && source[i] == output[j]) positions[i + 1].Add(j + 1);
                if (terms.TryGetValue(i, out var term) && Matches(term.Written, output, j)) positions[i + term.Count].Add(j + term.Written.Length);
            }
            foreach (var restart in abandoned.Where(restart => restart.Start == i)) positions[i + restart.Count].UnionWith(positions[i]);
        }
        return positions[source.Length].Contains(output.Length);
    }

    private static string JoinWords(string value) => Regex.Replace(value, @"(?<=\p{L})-(?=\p{L})", " ");
    private static string[] Words(string value) => WordPattern.Matches(value.ToLowerInvariant()).Select(match => match.Value).ToArray();
    private static string[] Tokens(string value) => TokenPattern.Matches(value).Select(match => match.Value.ToLowerInvariant().Replace('’', '\'')).ToArray();
    private static bool Matches(string[] phrase, string[] words, int index) => index + phrase.Length <= words.Length && words.AsSpan(index, phrase.Length).SequenceEqual(phrase);

    private static List<(int Start, int Count)> Restarts(string baseline)
    {
        var ranges = TokenPattern.Matches(baseline).ToArray();
        var source = Tokens(baseline);
        bool Separated(int index) => index == source.Length || baseline[(ranges[index - 1].Index + ranges[index - 1].Length)..ranges[index].Index].Any(Separators.Contains);
        var found = new List<(int, int)>();
        for (var start = 0; start < source.Length; start++)
        for (var count = 1; count <= 6 && start + count < source.Length; count++)
        {
            var next = start + count;
            var shared = 0;
            while (shared < count && next + shared < source.Length && source[start + shared] == source[next + shared]) shared++;
            if (shared == count)
            {
                if (count == 1 && !Separated(next)) continue;
                var end = next + count;
                var phrase = source[start..next];
                while (Matches(phrase, source, end)) end += count;
                if (!Separated(end)) found.Add((start, count));
            }
            else if (shared >= 2 && count - shared <= 2 && Separated(next)) found.Add((start, count));
        }
        return found;
    }
}
