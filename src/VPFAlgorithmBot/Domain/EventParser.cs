using System.Globalization;
using System.Text.RegularExpressions;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Domain;

public static partial class EventParser
{
    public sealed record Description(string ObjectCode, string ObjectName, string Name)
    {
        public string MatchText => $"{ObjectCode}: {Name}";
        public string MatchPattern => "^" + Regex.Escape(MatchText) + "$";
    }
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    [GeneratedRegex(@"(?<!\d)(?<h>\d{1,2}):(?<m>\d{2}):(?<s>\d{2})(?!\d)")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"(?:🔴|🟢)\s*\d{1,2}:\d{2}:\d{2}\s+(?<object>[^:\r\n]{1,80}):\s*(?<name>.+)", RegexOptions.Singleline)]
    private static partial Regex DescriptionPattern();

    public static string NormalizeObjectCode(string value) => Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", "");

    public static string NormalizeText(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    public static Description Describe(string text)
    {
        var match = DescriptionPattern().Match(text);
        if (!match.Success) throw new FormatException("Не вдалося визначити об’єкт і назву алгоритму.");
        var name = NormalizeText(match.Groups["name"].Value);
        // These suffixes contain changing measurements, not the rule's thresholds.
        name = Regex.Split(name, @"\s*\((?:Зафіксовано|Було\s*:|День туру\s*:)|\s*Тривалість\s*:", RegexOptions.IgnoreCase)[0];
        name = Regex.Replace(name, @"\bна протязі\b", "протягом", RegexOptions.IgnoreCase).Trim().TrimEnd('.').Trim();
        var display = NormalizeText(match.Groups["object"].Value).ToUpperInvariant();
        var code = NormalizeObjectCode(display);
        if (code.Length == 0 || code.Length > 50 || name.Length == 0 || name.Length > 300)
            throw new FormatException("Назва об’єкта або алгоритму порожня чи завелика.");
        var description = new Description(code, display, name);
        if (description.MatchPattern.Length > 500) throw new FormatException("Правило розпізнавання завелике.");
        return description;
    }

    public static TimeSpan? Duration(string text)
    {
        var match = Regex.Match(NormalizeText(text), @"Тривалість\s*:\s*(?:(?<d>\d+)d\s*)?(?:(?<h>\d+)h\s*)?(?:(?<m>\d+)m\s*)?(?:(?<s>\d+)s\s*)?$", RegexOptions.IgnoreCase);
        if (!match.Success || !new[] { "d", "h", "m", "s" }.Any(key => match.Groups[key].Success)) return null;
        double Value(string key) => match.Groups[key].Success ? double.Parse(match.Groups[key].Value, CultureInfo.InvariantCulture) : 0;
        return TimeSpan.FromSeconds(Value("d") * 86400 + Value("h") * 3600 + Value("m") * 60 + Value("s"));
    }

    public static bool Matches(AlgorithmRule rule, string text)
    {
        bool IsMatch(string candidate) => Regex.IsMatch(candidate, rule.MatchPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (IsMatch(text) || IsMatch(NormalizeText(text))) return true;
        try { return IsMatch(Describe(text).MatchText); }
        catch (FormatException) { return false; }
    }

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
        var matches = rules.Where(rule => rule.Enabled && Matches(rule, text))
            .OrderBy(rule => rule.Priority).ThenBy(rule => rule.Id).ToArray();
        if (matches.Length == 0) throw new FormatException("Немає правила розпізнавання.");
        if (matches.Length > 1 && matches[0].Priority == matches[1].Priority)
            throw new FormatException("Кілька правил мають однаковий пріоритет.");
        return matches[0];
    }
}
