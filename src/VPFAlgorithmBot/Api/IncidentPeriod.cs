using System.Globalization;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Api;

public static class IncidentPeriod
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    public static bool TryApply(IQueryable<Incident> source, string? period, string? fromDate, string? toDate,
        DateTimeOffset now, out IQueryable<Incident> filtered, out string? error)
    {
        filtered = source;
        error = null;
        if (string.IsNullOrWhiteSpace(period) || period == "all") return true;

        DateOnly from;
        DateOnly to;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Kyiv).Date);
        switch (period)
        {
            case "today": from = to = today; break;
            case "yesterday": from = to = today.AddDays(-1); break;
            case "range":
                if (!DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                    !DateOnly.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                {
                    error = "Для періоду за датами задайте початок і кінець у форматі РРРР-ММ-ДД.";
                    return false;
                }
                if (from > to)
                {
                    error = "Дата початку пізніша за дату завершення.";
                    return false;
                }
                if (to == DateOnly.MaxValue)
                {
                    error = "Некоректна кінцева дата.";
                    return false;
                }
                break;
            default:
                error = "Невідомий період.";
                return false;
        }

        var startUtc = KyivMidnightUtc(from);
        var endUtc = KyivMidnightUtc(to.AddDays(1));
        filtered = source.Where(x => x.StartedAtUtc >= startUtc && x.StartedAtUtc < endUtc);
        return true;
    }

    private static DateTimeOffset KyivMidnightUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Kyiv), TimeSpan.Zero);
    }
}
