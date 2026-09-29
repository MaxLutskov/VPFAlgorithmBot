using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Api;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS " + message);
}

var options = new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("tests-" + Guid.NewGuid().ToString("N")).Options;
await using var db = new AlgorithmDbContext(options);
await db.Database.EnsureCreatedAsync();
await DemoSeeder.SeedAsync(db);
var service = new IncidentService(db);
var started = new DateTimeOffset(2026, 9, 28, 7, 3, 0, TimeSpan.FromHours(3));
var first = new InboundEvent(-1001, 77, 101, started,
    "🔴 07:02:57 ВФС: Концентрація залишкового хлору менше 0,3 ppm на протязі 1 години. (Зафіксовано концентрацію: 0.13 ppm)");
var created = await service.IngestAsync(first);
Check(created.Status == "Created" && created.IncidentId is not null, "red message creates incident");
Check((await service.IngestAsync(first)).Status == "Duplicate", "duplicate message ignored");
var incidentId = created.IncidentId!.Value;
var green = new InboundEvent(-1001, 77, 102, new DateTimeOffset(2026, 9, 28, 9, 8, 0, TimeSpan.FromHours(3)),
    "🟢 09:07:57 ВФС: Концентрація залишкового хлору менше 0,3 ppm на протязі 1 години. Тривалість: 2h 5m 0s");
Check((await service.IngestAsync(green)).Status == "Resolved", "green message resolves incident");
var before = await db.Incidents.Include(x => x.Responses).SingleAsync(x => x.Id == incidentId);
Check(before.ProblemState == "Resolved" && before.AnswerState == "Unanswered", "resolved incident remains unanswered");
Check((before.EndedAtUtc!.Value - before.StartedAtUtc).TotalSeconds == 7500, "duration 2 hours 5 minutes");
var originalEnd = before.EndedAtUtc;
var saved = await service.RespondAsync(incidentId, 900002, "", 1, "test-answer-1");
var repeat = await service.RespondAsync(incidentId, 900002, "", 1, "test-answer-1");
Check(saved.Id == repeat.Id, "callback is idempotent");
var after = await db.Incidents.Include(x => x.Responses).SingleAsync(x => x.Id == incidentId);
Check(after.ProblemState == "Resolved" && after.AnswerState == "Answered", "late answer changes answer state");
Check(after.EndedAtUtc == originalEnd, "late answer does not change resolution time");
Check(after.Responses.Count == 1 && after.Responses[0].UserId == 2, "answer author is stored");
var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(3));
Check(IncidentPeriod.TryApply(db.Incidents, "today", null, null, now, out var today, out _) &&
    await today.CountAsync() == 1, "today uses Kyiv start date");
Check(IncidentPeriod.TryApply(db.Incidents, "yesterday", null, null, now, out var yesterday, out _) &&
    await yesterday.CountAsync() == 0, "yesterday excludes today's incident");
Check(IncidentPeriod.TryApply(db.Incidents, "range", "2026-09-28", "2026-09-28", now, out var range, out _) &&
    await range.CountAsync() == 1, "custom date range includes both bounds");
Check(!IncidentPeriod.TryApply(db.Incidents, "range", "2026-09-29", "2026-09-28", now, out _, out _),
    "reversed dates are rejected");
Check((await db.ResponseTemplates.Where(x => x.Enabled && x.AlgorithmRuleId == after.AlgorithmRuleId).CountAsync()) == 1,
    "only this algorithm's template is offered");
try
{
    await service.RespondAsync(incidentId, 900002, "", 2, "wrong-algorithm");
    throw new Exception("FAIL: another algorithm's template was allowed");
}
catch (UnauthorizedAccessException) { Console.WriteLine("PASS another algorithm's template denied"); }
try
{
    await service.RespondAsync(incidentId, 12345, "Unauthorized", null, "bad-answer");
    throw new Exception("FAIL: unauthorized user was allowed");
}
catch (UnauthorizedAccessException) { Console.WriteLine("PASS unauthorized user denied"); }
var otherChat = new InboundEvent(-9999, 77, 103, started, first.Text);
Check((await service.IngestAsync(otherChat)).Status == "Ignored", "unknown chat ignored");
Console.WriteLine("ALL DOMAIN TESTS PASSED");

await using var discoveryDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("discovery-" + Guid.NewGuid()).Options);
discoveryDb.Chats.Add(new SourceChat { TelegramChatId = -2001, SenderTelegramId = 77, Name = "РЧВ" });
await discoveryDb.SaveChangesAsync();
var discovery = new IncidentService(discoveryDb);
var unknownRed = first with { ChatTelegramId = -2001, Text = "🔴 07:02:57 РЧВ 7: Зміна дозування більше ніж на 25% (хлорація включено). (Було:\n2 мл/м3. Стало: 1.09 мл/м3)" };
Check((await discovery.IngestAsync(unknownRed with { SenderTelegramId = 88 })).Status == "Ignored" && !await discoveryDb.Objects.AnyAsync(), "untrusted sender cannot create catalog data");
Check((await discovery.IngestAsync(unknownRed with { Text = unknownRed.Text.Replace("07:02:57", "27:02:57") })).Status == "Review" && !await discoveryDb.Objects.AnyAsync(), "invalid event time cannot create catalog data");
unknownRed = unknownRed with { MessageTelegramId = 201 };
var autoCreated = await discovery.IngestAsync(unknownRed);
Check(autoCreated.Status == "Created" && await discoveryDb.Objects.CountAsync() == 1 && await discoveryDb.AlgorithmRules.CountAsync() == 1, "unknown object and algorithm are created automatically");
var autoRule = await discoveryDb.AlgorithmRules.SingleAsync();
Check(autoRule.Name.Contains("(хлорація включено)") && !autoRule.Name.Contains("Було"), "stable parentheses kept and measurements removed");
Check(await discoveryDb.ResponseTemplates.CountAsync() == 4 && await discoveryDb.TemplateVersions.CountAsync() == 4, "four independent versioned templates are created");
var autoGreen = unknownRed with { MessageTelegramId = 202, TelegramDateUtc = green.TelegramDateUtc,
    Text = "🟢 09:07:57 РЧВ7: Зміна дозування більше ніж на 25% (хлорація включено). (Було: 3 мл/м3. Стало: 0.5 мл/м3) Тривалість: 2h 5m 0s" };
Check((await discovery.IngestAsync(autoGreen)).Status == "Resolved" && await discoveryDb.AlgorithmRules.CountAsync() == 1, "changed measurements and object spacing pair with the same rule");
var secondType = await discovery.IngestAsync(unknownRed with { MessageTelegramId = 203, Text = unknownRed.Text.Replace("25%", "30%") });
Check(secondType.Status == "Created" && await discoveryDb.AlgorithmRules.CountAsync() == 2 && await discoveryDb.ResponseTemplates.CountAsync() == 8, "different thresholds create distinct algorithms and templates");
Check(EventParser.Describe(first.Text).MatchText == EventParser.Describe(first.Text.Replace("на протязі", "протягом").Replace("0.13", "0.26")).MatchText, "equivalent duration phrasing and observations normalize identically");
autoRule.Enabled = false;
await discoveryDb.SaveChangesAsync();
Check((await discovery.IngestAsync(unknownRed with { MessageTelegramId = 204 })).Status == "Review" && await discoveryDb.AlgorithmRules.CountAsync() == 2, "disabled rule is not recreated");
var discoveredObject = await discoveryDb.Objects.SingleAsync();
discoveredObject.Enabled = false;
await discoveryDb.SaveChangesAsync();
Check((await discovery.IngestAsync(unknownRed with { MessageTelegramId = 205, Text = unknownRed.Text.Replace("25%", "40%") })).Status == "Review", "disabled object is respected");
Check(EventParser.Duration("Тривалість: 2d 18h\n47m 15s") == new TimeSpan(2, 18, 47, 15), "multiday wrapped duration is parsed");

await using var historyDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("history-" + Guid.NewGuid()).Options);
var historyPayload = Environment.GetEnvironmentVariable("HistoryImport__GzipBase64");
var productionFixture = !string.IsNullOrWhiteSpace(historyPayload);
if (!productionFixture)
{
    var sample = new[]
    {
        new HistoricalSeed.SeedMessage("rchv", "28.09.2026 12:00", "🔴 12:00:00 ТЕСТ1: Тестовий алгоритм"),
        new HistoricalSeed.SeedMessage("rchv", "28.09.2026 12:15", "🟢 12:15:00 ТЕСТ1: Тестовий алгоритм Тривалість: 15m 0s"),
        new HistoricalSeed.SeedMessage("rchv", "28.09.2026 13:00", "🟢 13:00:00 ТЕСТ1: Тест відновлення початку Тривалість: 1d 0h 0m 0s"),
        new HistoricalSeed.SeedMessage("vfs-ns", "28.09.2026 14:00", "🔴 14:00:00 ТЕСТ2: Тест відсутності завершення")
    };
    using var output = new MemoryStream();
    using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        await System.Text.Json.JsonSerializer.SerializeAsync(gzip, sample);
    historyPayload = Convert.ToBase64String(output.ToArray());
}
var expectedMessages = productionFixture ? 104 : 4;
var expectedIncidents = productionFixture ? 57 : 3;
var expectedRules = productionFixture ? 36 : 3;
await HistoricalSeed.SeedAsync(historyDb, historyPayload);
Check(await historyDb.IncomingMessages.CountAsync() == expectedMessages && await historyDb.Incidents.CountAsync() == expectedIncidents, $"historical import: {expectedMessages} messages, {expectedIncidents} incidents");
Check(await historyDb.Objects.CountAsync() == (productionFixture ? 14 : 2) && await historyDb.AlgorithmRules.CountAsync() == expectedRules, "historical catalog has expected object and rule counts");
Check(await historyDb.ResponseTemplates.CountAsync() == expectedRules * 4 && await historyDb.TemplateVersions.CountAsync() == expectedRules * 4, "all historical algorithms have four individually stored templates");
Check(await historyDb.Incidents.CountAsync(x => x.EndedAtUtc != null) == (productionFixture ? 52 : 2) && await historyDb.Incidents.CountAsync(x => x.Quality == "historical_inferred_start") == (productionFixture ? 5 : 1), "completed incidents and reconstructed starts have expected counts");
Check(await historyDb.Incidents.CountAsync(x => x.Quality == "historical_missing_end" && x.EndedAtUtc == null) == (productionFixture ? 5 : 1), "missing recoveries remain explicitly incomplete");
Check(!await historyDb.NotificationOutbox.AnyAsync() && !await historyDb.Responses.AnyAsync() && !await historyDb.Chats.AnyAsync(x => x.Enabled), "history import creates neither notifications nor fabricated answers or enabled chat sources");
var reconstructed = await historyDb.Incidents.FirstAsync(x => x.Quality == "historical_inferred_start");
var recoverySource = await historyDb.IncomingMessages.FindAsync(reconstructed.EndMessageId);
Check(reconstructed.StartedAtUtc == reconstructed.EndedAtUtc - EventParser.Duration(recoverySource!.Text), "inferred start equals recovery time minus reported duration");
var changedTemplate = await historyDb.ResponseTemplates.FirstAsync();
changedTemplate.Text = "Адміністратор змінив відповідь";
changedTemplate.Enabled = false;
await historyDb.SaveChangesAsync();
await HistoricalSeed.SeedAsync(historyDb, historyPayload);
Check(await historyDb.Incidents.CountAsync() == expectedIncidents && await historyDb.IncomingMessages.CountAsync() == expectedMessages && await historyDb.ResponseTemplates.CountAsync() == expectedRules * 4 && changedTemplate.Text == "Адміністратор змінив відповідь" && !changedTemplate.Enabled, "redeployment is idempotent and preserves edited templates");
Console.WriteLine("ALL DISCOVERY AND HISTORY TESTS PASSED");
