using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Telegram;

public static class DailyDigestService
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    public static int[] ObjectIds(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.TryParse(x, out var id) ? id : 0).Where(x => x > 0).Distinct().ToArray();

    public static Task<int> AddInitialSchedulesAsync(AlgorithmDbContext db, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            var chats = await db.Chats.Where(x => x.Enabled).ToListAsync(ct);
            var objects = await db.Objects.Where(x => x.Enabled).ToListAsync(ct);
            var existing = await db.DailyDigestSchedules.Select(x => x.ChatId).ToListAsync(ct);
            var added = 0;
            foreach (var chat in chats.Where(x => !existing.Contains(x.Id)))
            {
                string[] families = chat.Name.Trim() switch
                {
                    var name when name.Equals("Алгоритми ВФС", StringComparison.OrdinalIgnoreCase) => ["ВФС", "НС"],
                    var name when name.Equals("Алгоритми ВДВП", StringComparison.OrdinalIgnoreCase) => ["РЧВ"],
                    _ => []
                };
                var ids = objects.Where(x => families.Contains(AlgorithmCatalog.Family(x.Code)))
                    .Select(x => x.Id).OrderBy(x => x).ToArray();
                if (ids.Length == 0) continue;
                db.DailyDigestSchedules.Add(new DailyDigestSchedule
                {
                    ChatId = chat.Id, LocalTime = "16:00", ObjectIdsCsv = string.Join(',', ids)
                });
                added++;
            }
            await db.SaveChangesAsync(ct);
            return added;
        }, ct);

    public static Task<int> QueueDueAsync(AlgorithmDbContext db, DateTimeOffset nowUtc, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            var local = TimeZoneInfo.ConvertTime(nowUtc, Kyiv);
            var date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var schedules = await db.DailyDigestSchedules.Where(x => x.Enabled).ToListAsync(ct);
            var chats = await db.Chats.AsNoTracking().Where(x => x.Enabled).ToDictionaryAsync(x => x.Id, ct);
            var queued = 0;
            foreach (var schedule in schedules)
            {
                if (!chats.ContainsKey(schedule.ChatId) || !TimeOnly.TryParseExact(schedule.LocalTime, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var at) || TimeOnly.FromDateTime(local.DateTime) < at) continue;
                if (await db.DailyDigestDeliveries.AnyAsync(x => x.ScheduleId == schedule.Id && x.LocalDate == date, ct)) continue;
                var ids = ObjectIds(schedule.ObjectIdsCsv);
                if (ids.Length == 0) continue;
                var incidents = await (from incident in db.Incidents.AsNoTracking()
                    join obj in db.Objects.AsNoTracking() on incident.ObjectId equals obj.Id
                    join rule in db.AlgorithmRules.AsNoTracking() on incident.AlgorithmRuleId equals rule.Id
                    where ids.Contains(incident.ObjectId) && obj.Enabled && incident.EndedAtUtc == null &&
                        !db.Responses.Any(response => response.IncidentId == incident.Id)
                    orderby incident.StartedAtUtc, incident.Id
                    select new { ObjectName = obj.Name, RuleName = rule.Name, incident.StartedAtUtc }).ToListAsync(ct);
                var lines = incidents.Select((x, i) =>
                    $"{i + 1}. {x.ObjectName} — {x.RuleName}\n   Початок: {TimeZoneInfo.ConvertTime(x.StartedAtUtc, Kyiv):dd.MM.yy HH:mm}").ToArray();
                var header = $"🔴 Активні алгоритми без відповіді — {local:dd.MM.yyyy}, {schedule.LocalTime} (Київ)\n";
                var parts = Split(header, lines);
                for (var part = 0; part < parts.Count; part++)
                    db.DailyDigestDeliveries.Add(new DailyDigestDelivery
                    {
                        ScheduleId = schedule.Id, LocalDate = date, Part = part + 1,
                        Text = parts[part], DueAtUtc = nowUtc
                    });
                queued++;
            }
            await db.SaveChangesAsync(ct);
            return queued;
        }, ct);

    private static List<string> Split(string header, string[] lines)
    {
        if (lines.Length == 0) return [header + "Наразі таких алгоритмів немає."];
        var parts = new List<string>();
        var current = new StringBuilder(header);
        foreach (var line in lines)
        {
            if (current.Length + line.Length + 2 > 3500)
            {
                parts.Add(current.ToString().TrimEnd());
                current.Clear().Append(header);
            }
            current.Append(line).Append('\n');
        }
        parts.Add(current.ToString().TrimEnd());
        return parts;
    }

    public static async Task DeliverDueAsync(AlgorithmDbContext db, TelegramClient telegram, bool demo,
        DateTimeOffset nowUtc, ILogger logger, CancellationToken ct = default)
    {
        var due = await db.DailyDigestDeliveries.Where(x => x.Status == "pending" && x.DueAtUtc <= nowUtc)
            .OrderBy(x => x.Id).Take(20).ToListAsync(ct);
        foreach (var item in due)
        {
            var schedule = await db.DailyDigestSchedules.FindAsync([item.ScheduleId], ct);
            var chat = schedule is null ? null : await db.Chats.FindAsync([schedule.ChatId], ct);
            if (schedule is null || !schedule.Enabled || chat is null || !chat.Enabled)
            {
                item.Status = "cancelled";
                continue;
            }
            try
            {
                if (!demo) await telegram.SendAsync(chat.TelegramChatId, item.Text, null, ct);
                item.Status = "sent";
                item.SentAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                item.Attempts++;
                item.LastError = ex.Message[..Math.Min(300, ex.Message.Length)];
                item.Status = item.Attempts >= 8 ? "failed" : "pending";
                item.DueAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, item.Attempts)));
                logger.LogWarning(ex, "Помилка доставки щоденного підсумку {DeliveryId}", item.Id);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
