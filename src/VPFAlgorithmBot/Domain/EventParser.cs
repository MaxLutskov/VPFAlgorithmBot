using System.Globalization;
using System.Text.RegularExpressions;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Domain;

public static partial class EventParser
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    [GeneratedRegex(@"(?<!\d)(?<h>\d{1,2}):(?<m>\d{2}):(?<s>\d{2})(?!\d)")]
    private static partial Regex TimePattern();

    public static string? Kind(string text)
    {
        if (text.Contains("🔴", StringComparison.Ordinal)) return "problem";
        if (text.Contains("🟢", StringComparison.Ordinal)) return "recovery";
        return null;
    }

    public static DateTimeOffset ParseTime(DateTimeOffset telegramDateUtc, string text)
    {
        var match = TimePattern().Match(text);
        if (!match.Success) throw new FormatException("У тексті не знайдено часу ГГ:ХХ:СС.");
        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59 || second > 59) throw new FormatException("Некоректний час у повідомленні.");
        var receivedLocal = TimeZoneInfo.ConvertTime(telegramDateUtc, Kyiv);
        var candidates = Enumerable.Range(-1, 3).Select(days => receivedLocal.Date.AddDays(days).Add(new TimeSpan(hour, minute, second)))
            .Where(value => !Kyiv.IsInvalidTime(value))
            .Select(value => new { Local = value, Utc = TimeZoneInfo.ConvertTimeToUtc(value, Kyiv) })
            .OrderBy(value => Math.Abs((value.Utc - telegramDateUtc.UtcDateTime).TotalSeconds)).ToList();
        if (candidates.Count == 0) throw new FormatException("Час потрапив у перехід годинника.");
        var chosen = candidates[0];
        if (Math.Abs((chosen.Utc - telegramDateUtc.UtcDateTime).TotalHours) > 6)
        {
            var sameDay = candidates.FirstOrDefault(x => x.Local.Date == receivedLocal.Date);
            if (sameDay is not null) chosen = sameDay;
        }
        if (Kyiv.IsAmbiguousTime(chosen.Local)) throw new FormatException("Неоднозначний час під час переходу годинника.");
        return new DateTimeOffset(chosen.Utc, TimeSpan.Zero);
    }

    public static AlgorithmRule MatchRule(IEnumerable<AlgorithmRule> rules, string text)
    {
        var matches = rules.Where(rule => rule.Enabled && Regex.IsMatch(text, rule.MatchPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            .OrderBy(rule => rule.Priority).ThenBy(rule => rule.Id).ToArray();
        if (matches.Length == 0) throw new FormatException("Немає правила розпізнавання.");
        if (matches.Length > 1 && matches[0].Priority == matches[1].Priority)
            throw new FormatException("Кілька правил мають однаковий пріоритет.");
        return matches[0];
    }
}
