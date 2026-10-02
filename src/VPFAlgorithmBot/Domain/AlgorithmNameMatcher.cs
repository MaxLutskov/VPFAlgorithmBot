using System.Text.RegularExpressions;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Domain;

// Compare the condition itself, while ignoring only its observation delay.
public static class AlgorithmNameMatcher
{
    private static readonly Regex Delay = new(@"\b(?:протягом|(?:на\s+)?протязі|за)\s+\d+\s*(?:х\s*)?(?:хв(?:илин\w*)?|год(?:ин\w*)?)\b|\b\d+\s*(?:х\s*)?(?:хв(?:илин\w*)?|год(?:ин\w*)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Number = new(@"\d+(?:[,.]\d+)?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Words = new(@"[^\p{L}\p{N}%]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string Key(string name)
    {
        var normalized = AlgorithmCatalog.RuleName(name).ToLowerInvariant()
            .Replace('’', '\'').Replace('ʼ', '\'');
        normalized = Regex.Replace(normalized, @"\bна\s+протязі\b", "протягом", RegexOptions.IgnoreCase);
        normalized = Delay.Replace(normalized, " ");
        normalized = normalized.Replace("відсунє", "відсутнє").Replace("відсуній", "відсутній");
        return Words.Replace(normalized, " ").Trim();
    }

    public static AlgorithmRule? Find(string name, IEnumerable<AlgorithmRule> rules, bool enabledOnly = false)
    {
        var key = Key(name);
        var candidates = rules.Where(x => !enabledOnly || x.Enabled).ToArray();
        var exact = candidates.Where(x => Key(x.Name) == key).OrderBy(x => x.Id).ToArray();
        if (exact.Length > 0) return exact[0];

        var numbers = Number.Matches(key).Select(x => x.Value.Replace(',', '.')).ToArray();
        var direction = Direction(key);
        var location = Location(key);
        var ranked = candidates.Select(x => (Rule: x, Key: Key(x.Name)))
            .Where(x => Number.Matches(x.Key).Select(y => y.Value.Replace(',', '.')).SequenceEqual(numbers) &&
                        Direction(x.Key) == direction && Location(x.Key) == location)
            .Select(x => (x.Rule, Score: Similarity(key, x.Key)))
            .OrderByDescending(x => x.Score).ToArray();
        if (ranked.Length == 0 || ranked[0].Score < 0.88 ||
            ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.06) return null;
        return ranked[0].Rule;
    }

    private static int Direction(string key) =>
        (Regex.IsMatch(key, @"\b(більше|більша|більший|вище)\b") ? 1 : 0) |
        (Regex.IsMatch(key, @"\b(менше|менша|менший|нижче)\b") ? 2 : 0) |
        (key.Contains("не змінюється", StringComparison.Ordinal) ? 4 : 0);

    private static int Location(string key) =>
        (key.Contains("вхідн", StringComparison.Ordinal) || key.Contains("на вході", StringComparison.Ordinal) ? 1 : 0) |
        (key.Contains("вихідн", StringComparison.Ordinal) || key.Contains("на виході", StringComparison.Ordinal) ? 2 : 0) |
        (key.Contains("основн", StringComparison.Ordinal) ? 4 : 0) |
        (key.Contains("підкачуюч", StringComparison.Ordinal) ? 8 : 0);

    private static double Similarity(string left, string right)
    {
        if (left == right) return 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1.0 - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}
