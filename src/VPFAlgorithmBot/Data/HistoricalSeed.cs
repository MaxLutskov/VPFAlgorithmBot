using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Data;

public static class HistoricalSeed
{
    public const string Marker = "import:telegram-history:2026-09:v1";
    public sealed record SeedMessage(string Source, string LocalDate, string Text);
    public sealed record ImportSummary(int Messages, int Incidents, int Resolved, int InferredStarts, int MissingEnds);

    public static async Task SeedAsync(AlgorithmDbContext db, string? compressedData, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(compressedData) || await db.Settings.AnyAsync(x => x.Key == Marker, ct)) return;
        using var bytes = new MemoryStream(Convert.FromBase64String(compressedData));
        using var stream = new GZipStream(bytes, CompressionMode.Decompress);
        var messages = await JsonSerializer.DeserializeAsync<SeedMessage[]>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct) ?? [];
        if (messages.Length == 0) throw new FormatException("Історичний імпорт не містить повідомлень.");
        await ImportAsync(db, messages, Marker, ct);
    }

    public static Task<ImportSummary?> ImportAsync(AlgorithmDbContext db, IReadOnlyList<SeedMessage> rows, string marker, CancellationToken ct = default) =>
        DatabaseWork.RunAsync<ImportSummary?>(db, async () =>
        {
            if (await db.Settings.AnyAsync(x => x.Key == marker, ct)) return null;
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
            var parsed = rows.Select((row, index) =>
            {
                var local = DateTime.ParseExact(row.LocalDate, "dd.MM.yyyy H:mm", CultureInfo.InvariantCulture);
                var sent = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
                return new { Row = row, Index = index, Sent = sent, Occurred = EventParser.ParseTime(sent, row.Text) };
            }).OrderBy(x => x.Occurred).ThenBy(x => x.Index).ToList();
            var chats = new Dictionary<string, SourceChat>();
            // No Telegram chat IDs are present in the pasted exports. Archive sources are disabled
            // and isolated from live chats, so they cannot receive messages or trigger routing.
            foreach (var source in rows.Select(x => x.Source).Distinct())
            {
                var telegramId = source switch
                {
                    "rchv" => -8_000_000_000_001L,
                    "vfs-ns" => -8_000_000_000_002L,
                    _ => throw new FormatException("Невідоме джерело історичного імпорту.")
                };
                var chat = await db.Chats.SingleOrDefaultAsync(x => x.TelegramChatId == telegramId, ct);
                if (chat is null)
                {
                    chat = new SourceChat { TelegramChatId = telegramId, Enabled = false,
                        Name = source == "rchv" ? "Архів: алгоритми РЧВ (вересень 2026)" : "Архів: алгоритми ВФС та НС (вересень 2026)" };
                    db.Chats.Add(chat);
                    await db.SaveChangesAsync(ct);
                }
                chats.Add(source, chat);
            }
            var ruleCache = new Dictionary<string, AlgorithmRule>();
            var imported = new List<Incident>();
            var open = new Dictionary<(int Chat, int Object, int Rule), Incident>();
            foreach (var item in parsed)
            {
                var description = EventParser.Describe(item.Row.Text);
                if (!ruleCache.TryGetValue(description.MatchText, out var rule))
                {
                    rule = await AlgorithmCatalog.ResolveAsync(db, item.Row.Text, ct, includeDisabled: true);
                    ruleCache.Add(description.MatchText, rule);
                }
                var chat = chats[item.Row.Source];
                var obj = await db.Objects.SingleAsync(x => x.Code == description.ObjectCode, ct);
                var message = new IncomingMessage
                {
                    ChatId = chat.Id, TelegramMessageId = -(item.Index + 1),
                    TelegramDateUtc = item.Sent, ReceivedAtUtc = DateTimeOffset.UtcNow,
                    Text = item.Row.Text, Status = "historical"
                };
                db.IncomingMessages.Add(message);
                await db.SaveChangesAsync(ct);
                var key = (chat.Id, obj.Id, rule.Id);
                Incident incident;
                if (EventParser.Kind(item.Row.Text) == "problem")
                {
                    if (open.ContainsKey(key)) throw new FormatException("Повторний початок в історичному імпорті.");
                    incident = new Incident
                    {
                        ChatId = chat.Id, ObjectId = obj.Id, CategoryId = rule.CategoryId,
                        AlgorithmRuleId = rule.Id, StartedAtUtc = item.Occurred,
                        StartMessageId = message.Id, Quality = "historical_missing_end"
                    };
                    db.Incidents.Add(incident);
                    imported.Add(incident);
                    open.Add(key, incident);
                }
                else if (EventParser.Kind(item.Row.Text) == "recovery")
                {
                    var duration = EventParser.Duration(item.Row.Text);
                    if (open.Remove(key, out var existing))
                    {
                        incident = existing;
                        incident.Quality = "historical";
                        if (duration is not null && Math.Abs((item.Occurred - incident.StartedAtUtc - duration.Value).TotalSeconds) > 60)
                            incident.Quality = "historical_duration_mismatch";
                    }
                    else
                    {
                        if (duration is null) throw new FormatException("Завершення без початку та тривалості в історії.");
                        incident = new Incident
                        {
                            ChatId = chat.Id, ObjectId = obj.Id, CategoryId = rule.CategoryId,
                            AlgorithmRuleId = rule.Id, StartedAtUtc = item.Occurred - duration.Value,
                            StartMessageId = message.Id, Quality = "historical_inferred_start"
                        };
                        db.Incidents.Add(incident);
                        imported.Add(incident);
                    }
                    incident.EndedAtUtc = item.Occurred;
                    incident.EndMessageId = message.Id;
                }
                else throw new FormatException("Історичне повідомлення без маркера стану.");
                await db.SaveChangesAsync(ct);
                message.IncidentId = incident.Id;
            }
            // Seed only empty template sets; never restore deleted choices or overwrite edited text
            // on later app deployments (the import marker and this data commit atomically).
            var canonical = await AlgorithmCatalog.CanonicalIdsAsync(db, ct);
            var canonicalIds = canonical.Values.Distinct().ToArray();
            foreach (var rule in await db.AlgorithmRules.Where(x => canonicalIds.Contains(x.Id)).ToListAsync(ct))
                await AlgorithmCatalog.AddTestTemplatesAsync(db, rule, ct);
            var summary = new ImportSummary(rows.Count, imported.Count, imported.Count(x => x.EndedAtUtc != null),
                imported.Count(x => x.Quality == "historical_inferred_start"), open.Count);
            db.Settings.Add(new Setting { Key = marker, Value = JsonSerializer.Serialize(summary) });
            db.AuditEvents.Add(new AuditEvent { Action = "import-history", Entity = marker,
                Detail = JsonSerializer.Serialize(summary), CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            return summary;
        }, ct);
}
