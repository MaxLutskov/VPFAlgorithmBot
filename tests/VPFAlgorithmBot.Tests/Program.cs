using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Api;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;
using VPFAlgorithmBot.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

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
var channelUpdate = System.Text.Json.JsonSerializer.Deserialize<TelegramUpdate>("""{"channel_post":{"message_id":104,"date":1790580000,"chat":{"id":-1001,"type":"channel"},"text":"🟢 09:07:57 ВФС: Тест Тривалість: 1m 0s"}}""");
Check(channelUpdate?.ChannelPost?.Chat.Id == -1001 && channelUpdate.ChannelPost.MessageId == 104,
    "Telegram channel_post updates are decoded");
Console.WriteLine("ALL DOMAIN TESTS PASSED");

await using (var routingDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("routing-" + Guid.NewGuid()).Options))
{
    await DemoSeeder.SeedAsync(routingDb);
    var alternateChat = new SourceChat { TelegramChatId = -1002, Name = "Інший чат" };
    var secondOperator = new AppUser { TelegramId = 900003, DisplayName = "Другий оператор", Status = "approved" };
    var legacyOperator = new AppUser { TelegramId = 900004, DisplayName = "Старе правило", Status = "approved" };
    routingDb.AddRange(alternateChat, secondOperator, legacyOperator);
    await routingDb.SaveChangesAsync();
    var vfsId = await routingDb.Objects.Where(x => x.Code == "ВФС").Select(x => x.Id).SingleAsync();
    routingDb.RouteRules.AddRange(
        new RouteRule { UserId = secondOperator.Id, ObjectId = vfsId, ChatId = alternateChat.Id, CategoryId = 999, Priority = 999 },
        new RouteRule { UserId = legacyOperator.Id, ChatId = (await routingDb.Chats.SingleAsync(x => x.TelegramChatId == -1001)).Id });
    await routingDb.SaveChangesAsync();
    Check((await new IncidentService(routingDb).IngestAsync(first)).Status == "Created", "routing fixture creates incident");
    var notified = await routingDb.NotificationOutbox.Where(x => x.Kind == "problem").Select(x => x.UserId).ToListAsync();
    Check(notified.Contains(secondOperator.Id) && !notified.Contains(legacyOperator.Id),
        "object assignments notify regardless of chat, category and priority; chat-only legacy rule is ignored");
}

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
var expectedRules = productionFixture ? 26 : 3;
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
Check(await LegacyArchiveCleanup.ApplyAsync(historyDb) == expectedIncidents &&
    !await historyDb.Incidents.AnyAsync() && !await historyDb.IncomingMessages.AnyAsync() && !await historyDb.Chats.AnyAsync(),
    "legacy archive incidents, messages and chats are removed");
Check(await LegacyArchiveCleanup.ApplyAsync(historyDb) == 0 && changedTemplate.Text == "Адміністратор змінив відповідь" &&
    await historyDb.AlgorithmRules.CountAsync() == expectedRules,
    "archive cleanup is idempotent and preserves the edited algorithm catalog");

await using var exportDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("export-" + Guid.NewGuid()).Options);
await DemoSeeder.SeedAsync(exportDb);
var exportService = new IncidentService(exportDb);
var exportChat = await exportDb.Chats.SingleAsync();
var exportNow = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
var exportMessages = new
{
    name = "Алгоритми ВФС",
    messages = new object[]
    {
        new { id = 900, type = "message", date_unixtime = exportNow.AddDays(-4).ToUnixTimeSeconds().ToString(), from_id = "user77", text = (object)"🔴 10:00:00 ВФС: Застарілий запис" },
        new { id = 901, type = "message", date_unixtime = new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), from_id = "user77", text = (object)new object[] { "🔴 10:00:00 ВФС: ", new { type = "bold", text = "Тест імпорту" } } },
        new { id = 902, type = "message", date_unixtime = new DateTimeOffset(2026, 9, 29, 7, 15, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), from_id = "user77", text = (object)"🟢 10:15:00 ВФС: Тест імпорту Тривалість: 15m 0s" },
        new { id = 903, type = "message", date_unixtime = new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), from_id = "user999", text = (object)"🔴 11:00:00 ВФС: Чужий відправник" },
        new { id = 904, type = "message", date_unixtime = new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), from_id = "user77", text = (object)"🟢 12:00:00 ВФС: Самовідновлення Тривалість: 30m 0s" }
    }
};
using var exportJson = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(exportMessages));
var exportResult = await TelegramDesktopImport.ImportAsync(exportJson, exportChat, exportService, exportNow);
Check(exportResult.EligibleMessages == 3 && exportResult.ImportedMessages == 3 &&
    await exportDb.Incidents.CountAsync() == 2 && await exportDb.NotificationOutbox.CountAsync() == 0,
    "Telegram Desktop export imports only last 72 hours and sends no old notifications");
Check(await exportDb.Incidents.AnyAsync(x => x.Quality == "export_complete") &&
    await exportDb.Incidents.AnyAsync(x => x.Quality == "export_inferred_start"),
    "exported red/green messages pair and green-only duration restores the start");
exportJson.Position = 0;
var repeatedExport = await TelegramDesktopImport.ImportAsync(exportJson, exportChat, exportService, exportNow);
Check(repeatedExport.Duplicates == 3 && await exportDb.Incidents.CountAsync() == 2, "repeated export does not duplicate incidents");

await using var overlapDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("live-export-overlap-" + Guid.NewGuid()).Options);
await DemoSeeder.SeedAsync(overlapDb);
var overlapService = new IncidentService(overlapDb);
var liveStart = await overlapService.IngestAsync(first);
await overlapService.IngestAsync(green);
var overlapChat = await overlapDb.Chats.SingleAsync();
var overlapExport = new { messages = new object[]
{
    new { id = 990, type = "message", date_unixtime = started.ToUnixTimeSeconds().ToString(), from_id = "user77", text = first.Text },
    new { id = 991, type = "message", date_unixtime = green.TelegramDateUtc.ToUnixTimeSeconds().ToString(), from_id = "user77", text = green.Text }
} };
using var overlapJson = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(overlapExport));
var overlapResult = await TelegramDesktopImport.ImportAsync(overlapJson, overlapChat, overlapService, exportNow);
Check(overlapResult.Duplicates == 2 && await overlapDb.Incidents.CountAsync() == 1 &&
    await overlapDb.IncomingMessages.CountAsync(x => x.IncidentId == liveStart.IncidentId) == 4,
    "Telegram export with different message IDs reuses the live incident");

var exportedStart = new InboundEvent(-1001, 77, 1201, new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.Zero),
    "🔴 10:00:00 ВФС: Тест імпорту");
var exportedEnd = new InboundEvent(-1001, 77, 1202, new DateTimeOffset(2026, 9, 29, 7, 15, 0, TimeSpan.Zero),
    "🟢 10:15:00 ВФС: Тест імпорту Тривалість: 15m 0s");
Check((await exportService.IngestAsync(exportedStart)).Status == "Duplicate" &&
    (await exportService.IngestAsync(exportedEnd)).Status == "Duplicate" &&
    await exportDb.Incidents.CountAsync() == 2,
    "live updates after import do not duplicate the imported incident");


var localExportPath = Environment.GetEnvironmentVariable("VPF_TEST_EXPORT_PATH");
if (!string.IsNullOrWhiteSpace(localExportPath))
{
    await using var realDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
        .UseInMemoryDatabase("real-export-" + Guid.NewGuid()).Options);
    var realChat = new SourceChat { TelegramChatId = -5307285804, SenderTelegramId = 8924298166,
        Name = "Алгоритми ВФС" };
    realDb.Chats.Add(realChat);
    await realDb.SaveChangesAsync();
    await using var realFile = File.OpenRead(localExportPath);
    var realResult = await TelegramDesktopImport.ImportAsync(realFile, realChat, new IncidentService(realDb), DateTimeOffset.UtcNow);
    Check(realResult.EligibleMessages > 0 && realResult.ImportedMessages > 0 &&
        await realDb.Incidents.AnyAsync(),
        $"local VFS export imports incidents (eligible={realResult.EligibleMessages}, imported={realResult.ImportedMessages}, review={realResult.NeedsReview})");
    Console.WriteLine($"Local VFS export: resolved={await realDb.Incidents.CountAsync(x => x.EndedAtUtc != null)}, active={await realDb.Incidents.CountAsync(x => x.EndedAtUtc == null)}");
}

await using var sharedDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("shared-" + Guid.NewGuid()).Options);
var sharedChat = new SourceChat { TelegramChatId = -3300, SenderTelegramId = 77, Name = "Алгоритми РЧВ" };
var rchv7 = new MonitoredObject { Code = "РЧВ7", Name = "РЧВ 7" };
var rchv2 = new MonitoredObject { Code = "РЧВ2", Name = "РЧВ 2" };
var sharedCategory = new ProblemCategory { Name = "Тиск" };
sharedDb.AddRange(sharedChat, rchv7, rchv2, sharedCategory);
await sharedDb.SaveChangesAsync();
var old7 = new AlgorithmRule { ObjectId = rchv7.Id, CategoryId = sharedCategory.Id, Name = "Високий тиск протягом 30 хв", MatchPattern = @"РЧВ7:\s*Високий тиск" };
var old2 = new AlgorithmRule { ObjectId = rchv2.Id, CategoryId = sharedCategory.Id, Name = "Високий тиск протягом 30 хв", MatchPattern = @"РЧВ2:\s*Високий тиск" };
sharedDb.AlgorithmRules.AddRange(old7, old2);
await sharedDb.SaveChangesAsync();
var oldTemplate7 = new ResponseTemplate { AlgorithmRuleId = old7.Id, ObjectId = rchv7.Id, Title = "Показники перевірено", Text = "Старий текст", Version = 1 };
var oldTemplate2 = new ResponseTemplate { AlgorithmRuleId = old2.Id, ObjectId = rchv2.Id, Title = "Показники перевірено", Text = "Уточнений текст для спільного типу", Version = 3 };
sharedDb.ResponseTemplates.AddRange(oldTemplate7, oldTemplate2);
await sharedDb.SaveChangesAsync();
var started7 = new IncomingMessage { ChatId = sharedChat.Id, TelegramMessageId = 1, Text = "історія" };
var started2 = new IncomingMessage { ChatId = sharedChat.Id, TelegramMessageId = 2, Text = "історія" };
sharedDb.IncomingMessages.AddRange(started7, started2);
await sharedDb.SaveChangesAsync();
var legacyIncident7 = new Incident { ChatId = sharedChat.Id, ObjectId = rchv7.Id, CategoryId = sharedCategory.Id, AlgorithmRuleId = old7.Id, StartMessageId = started7.Id, StartedAtUtc = started };
var legacyIncident2 = new Incident { ChatId = sharedChat.Id, ObjectId = rchv2.Id, CategoryId = sharedCategory.Id, AlgorithmRuleId = old2.Id, StartMessageId = started2.Id, StartedAtUtc = started };
sharedDb.Incidents.AddRange(legacyIncident7, legacyIncident2);
await sharedDb.SaveChangesAsync();
sharedDb.Responses.Add(new IncidentResponse { IncidentId = legacyIncident7.Id, UserId = 2, TemplateId = oldTemplate7.Id, TemplateVersion = 1, Text = "Відповідь з історії", CreatedAtUtc = started });
await sharedDb.SaveChangesAsync();
Check(await SharedAlgorithmUpgrade.ApplyAsync(sharedDb) == 1, "legacy RCHV types are consolidated once");
Check(await SharedAlgorithmUpgrade.ApplyAsync(sharedDb) == 0, "shared catalog upgrade is idempotent");
Check(legacyIncident7.AlgorithmRuleId == legacyIncident2.AlgorithmRuleId && legacyIncident7.ObjectId != legacyIncident2.ObjectId,
    "historical incidents use one type while preserving each site");
Check(await sharedDb.Responses.CountAsync(x => x.TemplateId == oldTemplate7.Id) == 1 && oldTemplate7.Id != oldTemplate2.Id,
    "historical answers retain their original template references");
Check(oldTemplate2.AlgorithmRuleId == legacyIncident7.AlgorithmRuleId && oldTemplate2.Enabled && !oldTemplate7.Enabled && oldTemplate2.ObjectId == null,
    "most recently edited answer becomes the shared choice without losing old versions");
Check(AlgorithmCatalog.CanonicalIds(await sharedDb.AlgorithmRules.ToListAsync(), await sharedDb.Objects.ToListAsync()).Values.Distinct().Count() == 1,
    "admin catalog presents one RCHV algorithm type");
old7.Enabled = false;
await sharedDb.SaveChangesAsync();
Check(AlgorithmCatalog.CanonicalIds(await sharedDb.AlgorithmRules.ToListAsync(), await sharedDb.Objects.ToListAsync())[old2.Id] == old7.Id,
    "disabling a shared type never promotes a legacy alias");
old7.Enabled = true;
await sharedDb.SaveChangesAsync();
var sharedService = new IncidentService(sharedDb);
var newFor7 = await sharedService.IngestAsync(new InboundEvent(-3300, 77, 11, started.AddHours(1), "🔴 08:02:57 РЧВ 7: Новий спільний алгоритм"));
var newFor2 = await sharedService.IngestAsync(new InboundEvent(-3300, 77, 12, started.AddHours(1), "🔴 08:02:57 РЧВ 2: Новий спільний алгоритм"));
Check(newFor7.Status == "Created" && newFor2.Status == "Created" &&
    (await sharedDb.Incidents.FindAsync(newFor7.IncidentId))!.AlgorithmRuleId == (await sharedDb.Incidents.FindAsync(newFor2.IncidentId))!.AlgorithmRuleId,
    "two RCHV sites can open the same algorithm independently in one chat");
Check((await sharedService.IngestAsync(new InboundEvent(-3300, 77, 13, started.AddHours(2), "🟢 09:02:57 РЧВ 2: Новий спільний алгоритм Тривалість: 1h 0m 0s"))).Status == "Resolved" &&
    (await sharedDb.Incidents.FindAsync(newFor7.IncidentId))!.EndedAtUtc is null,
    "recovery for RCHV 2 does not close RCHV 7");
Console.WriteLine("ALL SHARED ALGORITHM TESTS PASSED");

var failingTelegram = new CapturingTelegramHandler();
var telegramConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Telegram:BotToken"] = "test-token" }).Build();
var botUpdates = new BotUpdateService(db, service, new TelegramClient(new HttpClient(failingTelegram), telegramConfig),
    telegramConfig, NullLogger<BotUpdateService>.Instance);
await botUpdates.HandleAsync(new TelegramUpdate { Message = new TelegramMessage
{
    Chat = new TelegramChat { Id = 900001, Type = "private" },
    From = new TelegramUser { Id = 900001, FirstName = "Test" },
    MessageId = 9001, Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text = "/start"
} }, CancellationToken.None);
Check(failingTelegram.Body is not null && !failingTelegram.Body.Contains("reply_markup"),
    "failed private Telegram reply is acknowledged without a webhook retry or null reply_markup");

var answerOptions = new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("answer-flow-" + Guid.NewGuid()).Options;
await using var answerDb = new AlgorithmDbContext(answerOptions);
await DemoSeeder.SeedAsync(answerDb);
var answerService = new IncidentService(answerDb);
var opened = await answerService.IngestAsync(first);
var answerId = opened.IncidentId!.Value;
Check(await answerDb.NotificationOutbox.CountAsync(x => x.IncidentId == answerId && x.Kind == "chat_prompt") == 1 &&
    System.Text.Json.JsonSerializer.Serialize(ResponseKeyboard.Build(answerId,
        await answerDb.ResponseTemplates.Where(x => x.AlgorithmRuleId == 1).ToListAsync())).Contains($"template:{answerId}:1"),
    "new live algorithm queues one group prompt with answer buttons");
var recordingTelegram = new RecordingTelegramHandler();
var answerTelegram = new TelegramClient(new HttpClient(recordingTelegram), telegramConfig);
var answerUpdates = new BotUpdateService(answerDb, answerService, answerTelegram, telegramConfig, NullLogger<BotUpdateService>.Instance);
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "choose", From = new TelegramUser { Id = 900002 }, Data = $"templates:{answerId}" } }, CancellationToken.None);
Check(recordingTelegram.Bodies.Any(x => System.Text.Json.JsonDocument.Parse(x).RootElement.GetRawText().Contains("custom:")) &&
    recordingTelegram.Bodies.All(x => !System.Text.Json.JsonDocument.Parse(x).RootElement.GetProperty("text").GetString()!.Contains($"Алгоритм №{answerId}")),
    "response choices include custom answer without a visible algorithm number");
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "custom", From = new TelegramUser { Id = 900002 }, Data = $"custom:{answerId}" } }, CancellationToken.None);
Check(await answerDb.PendingCustomAnswers.AnyAsync(x => x.IncidentId == answerId), "custom answer selection is persisted");
var resumedUpdates = new BotUpdateService(answerDb, answerService, answerTelegram, telegramConfig, NullLogger<BotUpdateService>.Instance);
var plainAnswer = new TelegramUpdate { Message = new TelegramMessage
    { Chat = new TelegramChat { Id = 900002, Type = "private" }, From = new TelegramUser { Id = 900002 },
      MessageId = 22001, Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text = "Перевірено дозатор, роботу відновлено" } };
await resumedUpdates.HandleAsync(plainAnswer, CancellationToken.None);
await resumedUpdates.HandleAsync(plainAnswer, CancellationToken.None);
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId && x.Text == "Перевірено дозатор, роботу відновлено") == 1 &&
    !await answerDb.PendingCustomAnswers.AnyAsync(), "plain custom answer is stored once after service restart");

var groupMessage = new TelegramMessage { Chat = new TelegramChat { Id = -1001, Type = "supergroup" } };
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "other-group", From = new TelegramUser { Id = 900002 },
      Message = new TelegramMessage { Chat = new TelegramChat { Id = -9999, Type = "supergroup" } },
      Data = $"template:{answerId}:1" } }, CancellationToken.None);
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId) == 1,
    "buttons from another chat cannot answer this algorithm");
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "group-template-operator", From = new TelegramUser { Id = 900002 }, Message = groupMessage,
      Data = $"template:{answerId}:1" } }, CancellationToken.None);
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "group-template-admin", From = new TelegramUser { Id = 900001 }, Message = groupMessage,
      Data = $"template:{answerId}:1" } }, CancellationToken.None);
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId) == 3 &&
    await answerDb.Responses.Where(x => x.IncidentId == answerId).Select(x => x.UserId).Distinct().CountAsync() == 2,
    "two authorized people may each answer the same algorithm from the group");
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "group-template-admin", From = new TelegramUser { Id = 900001 }, Message = groupMessage,
      Data = $"template:{answerId}:1" } }, CancellationToken.None);
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId) == 3,
    "repeated group callback does not create a duplicate answer");
await answerUpdates.HandleAsync(new TelegramUpdate { CallbackQuery = new TelegramCallback
    { Id = "group-custom", From = new TelegramUser { Id = 900002, FirstName = "Оператор" }, Message = groupMessage,
      Data = $"custom:{answerId}" } }, CancellationToken.None);
var groupPending = await answerDb.PendingCustomAnswers.SingleAsync(x => x.IncidentId == answerId);
Check(groupPending.ChatTelegramId == -1001 && groupPending.PromptMessageTelegramId is not null,
    "group custom answer is tied to the requesting worker and bot prompt");
var unrelatedMessage = new TelegramUpdate { Message = new TelegramMessage
    { Chat = groupMessage.Chat, From = new TelegramUser { Id = 900002 }, MessageId = 4401,
      ReplyToMessage = new TelegramMessage { MessageId = groupPending.PromptMessageTelegramId!.Value + 1 },
      Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text = "Стороння розмова" } };
await answerUpdates.HandleAsync(unrelatedMessage, CancellationToken.None);
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId) == 3,
    "ordinary group conversation is not captured as an answer");
var groupReply = new TelegramUpdate { Message = new TelegramMessage
    { Chat = groupMessage.Chat, From = new TelegramUser { Id = 900002 }, MessageId = 4402,
      ReplyToMessage = new TelegramMessage { MessageId = groupPending.PromptMessageTelegramId!.Value },
      Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text = "Показники перевірено на місці" } };
await answerUpdates.HandleAsync(groupReply, CancellationToken.None);
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId && x.Text == "Показники перевірено на місці") == 1 &&
    !await answerDb.PendingCustomAnswers.AnyAsync(), "reply to the group prompt records exactly one custom answer");
await using var concurrentDb1 = new AlgorithmDbContext(answerOptions);
await using var concurrentDb2 = new AlgorithmDbContext(answerOptions);
await Task.WhenAll(
    new IncidentService(concurrentDb1).RespondAsync(answerId, 900001, "Перевірив оператор 1", null, "concurrent:1"),
    new IncidentService(concurrentDb2).RespondAsync(answerId, 900002, "Перевірив оператор 2", null, "concurrent:2"));
Check(await answerDb.Responses.CountAsync(x => x.IncidentId == answerId && x.ActionKey!.StartsWith("concurrent:")) == 2,
    "simultaneous answers by different users are both preserved");
var receipts = await answerDb.NotificationOutbox.Where(x => x.IncidentId == answerId && x.Kind.StartsWith("response_receipt:"))
    .OrderBy(x => x.Id).ToListAsync();
Check(receipts.Count == 3 && receipts.Any(x => x.Text.Contains("Показники перевірено на місці")) &&
    receipts.Any(x => x.Text.Contains("Проводиться перевірка показників")),
    "different group answers each queue their own receipt with the actual answer text");
await answerTelegram.SendReplyAsync(-1001, first.MessageTelegramId, receipts[0].Text, CancellationToken.None);
Check(recordingTelegram.Bodies.Any(body =>
    System.Text.Json.JsonDocument.Parse(body).RootElement.TryGetProperty("reply_parameters", out var reply) &&
    reply.GetProperty("message_id").GetInt32() == first.MessageTelegramId),
    "confirmation is sent as a reply to the original algorithm message");

var welcomeOptions = new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("welcome-flow-" + Guid.NewGuid()).Options;
await using var welcomeDb = new AlgorithmDbContext(welcomeOptions);
await DemoSeeder.SeedAsync(welcomeDb);
var welcomeTelegram = new RecordingTelegramHandler();
var welcomeClient = new TelegramClient(new HttpClient(welcomeTelegram), telegramConfig);
var welcomeConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Telegram:BotToken"] = "test-token", ["Telegram:Mode"] = "Webhook" }).Build();
using var welcomeServices = new ServiceCollection()
    .AddScoped(_ => new AlgorithmDbContext(welcomeOptions))
    .AddSingleton(welcomeClient)
    .BuildServiceProvider();
var welcomeWorker = new NotificationWorker(welcomeServices.GetRequiredService<IServiceScopeFactory>(),
    welcomeConfig, NullLogger<NotificationWorker>.Instance);
await welcomeWorker.StartAsync(CancellationToken.None);
for (var i = 0; i < 60 && !await welcomeDb.ChatInstructionOutbox.AnyAsync(x => x.Kind == "initial" && x.Status == "sent"); i++)
    await Task.Delay(50);
await welcomeWorker.StopAsync(CancellationToken.None);
Check(await welcomeDb.ChatInstructionOutbox.CountAsync(x => x.Kind == "initial" && x.Status == "sent") == 1 &&
    welcomeTelegram.Bodies.Count(body =>
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("chat_id").GetInt64() == -1001) == 1,
    "existing configured chat receives the short instruction once");
var welcomeUpdates = new BotUpdateService(welcomeDb, new IncidentService(welcomeDb), welcomeClient,
    telegramConfig, NullLogger<BotUpdateService>.Instance);
var join = System.Text.Json.JsonSerializer.Deserialize<TelegramUpdate>("""
    {"message":{"message_id":991,"date":1790840000,"chat":{"id":-1001,"type":"supergroup"},
    "new_chat_members":[{"id":900050,"first_name":"Новий працівник","is_bot":false}]}}
    """)!;
await welcomeUpdates.HandleAsync(join, CancellationToken.None);
await welcomeUpdates.HandleAsync(join, CancellationToken.None);
Check(await welcomeDb.ChatInstructionOutbox.CountAsync(x => x.Kind == "member_join" && x.EventMessageId == 991) == 1,
    "new member event queues one instruction even when Telegram retries it");
var joinWorker = new NotificationWorker(welcomeServices.GetRequiredService<IServiceScopeFactory>(),
    welcomeConfig, NullLogger<NotificationWorker>.Instance);
await joinWorker.StartAsync(CancellationToken.None);
for (var i = 0; i < 60 && !await welcomeDb.ChatInstructionOutbox.AnyAsync(x => x.Kind == "member_join" && x.Status == "sent"); i++)
    await Task.Delay(50);
await joinWorker.StopAsync(CancellationToken.None);
Check(await welcomeDb.ChatInstructionOutbox.CountAsync(x => x.Kind == "member_join" && x.Status == "sent") == 1 &&
    welcomeTelegram.Bodies.Count(body =>
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("chat_id").GetInt64() == -1001) == 2,
    "new member instruction is delivered and initial one is not repeated");

await using var accessDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("access-request-" + Guid.NewGuid()).Options);
await DemoSeeder.SeedAsync(accessDb);
var accessTelegram = new RecordingTelegramHandler();
var accessUpdates = new BotUpdateService(accessDb, new IncidentService(accessDb),
    new TelegramClient(new HttpClient(accessTelegram), telegramConfig), telegramConfig, NullLogger<BotUpdateService>.Instance);
var accessRequest = new TelegramUpdate { Message = new TelegramMessage
    { Chat = new TelegramChat { Id = 900010, Type = "private" },
      From = new TelegramUser { Id = 900010, FirstName = "Нова", LastName = "Людина", Username = "new_worker" },
      MessageId = 31001, Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Text = "/start" } };
await accessUpdates.HandleAsync(accessRequest, CancellationToken.None);
await accessUpdates.HandleAsync(accessRequest, CancellationToken.None);
Check(await accessDb.Users.CountAsync(x => x.TelegramId == 900010 && x.Status == "pending") == 1,
    "access request creates one pending user");
Check(accessTelegram.Bodies.Count(body =>
    System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("chat_id").GetInt64() == 900001) == 1,
    "new access request notifies the administrator only once");

await using var waterDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("water-catalog-" + Guid.NewGuid()).Options);
await waterDb.Database.EnsureCreatedAsync();
var waterObject = new MonitoredObject { Code = "РЧВ7", Name = "РЧВ 7" };
var waterCategory = new ProblemCategory { Name = "Без категорії" };
waterDb.Objects.Add(waterObject);
waterDb.Categories.Add(waterCategory);
await waterDb.SaveChangesAsync();
var existingWaterRule = new AlgorithmRule { ObjectId = waterObject.Id, CategoryId = waterCategory.Id,
    Name = "Засувка №1 знаходиться у ручному режимі протягом 45 хв", MatchPattern = "legacy" };
waterDb.AlgorithmRules.Add(existingWaterRule);
await waterDb.SaveChangesAsync();
await AlgorithmCatalog.AddTestTemplatesAsync(waterDb, existingWaterRule);
waterDb.ResponseTemplates.Add(new ResponseTemplate { AlgorithmRuleId = existingWaterRule.Id,
    Title = "Колишня власна відповідь", Text = "Цього варіанта немає у файлі" });
await waterDb.SaveChangesAsync();
var waterReport = await WaterWorkbookSeed.ApplyAsync(waterDb);
Console.WriteLine("WATER RECONCILIATION " + waterReport);
Check(System.Text.Json.JsonDocument.Parse(waterReport).RootElement.GetProperty("Rows").GetInt32() == 69 &&
    await waterDb.AlgorithmRules.CountAsync(x => x.Name.Contains("Засувка №1 знаходиться у ручному режимі")) == 1 &&
    await waterDb.ResponseTemplates.AnyAsync(x => x.AlgorithmRuleId == existingWaterRule.Id && x.Text == "Несправний перемикач") &&
    await waterDb.ResponseTemplates.CountAsync(x => x.AlgorithmRuleId == existingWaterRule.Id && x.Enabled) == 3,
    "water workbook matches the existing duration variant and activates only file answers");
Check(!await waterDb.ResponseTemplates.AnyAsync(x => x.AlgorithmRuleId == existingWaterRule.Id && x.Enabled &&
    x.Text == "Цього варіанта немає у файлі"), "previous custom answers outside the workbook are disabled");
Check(await waterDb.AlgorithmRules.CountAsync(x => x.Name.Contains("споживання води по") && x.ObjectId == waterObject.Id) == 2,
    "input and output water meter conditions remain separate algorithms");
var waterRuleCount = await waterDb.AlgorithmRules.CountAsync();
var waterTemplateCount = await waterDb.ResponseTemplates.CountAsync();
Check(await waterDb.AlgorithmRules.CountAsync(x => x.Name.Contains("Відсутній зв'язок з ПЛК") && x.ObjectId == waterObject.Id) == 1,
    "20-minute and one-hour PLC delay map to one water-supply algorithm");
await WaterWorkbookSeed.ApplyAsync(waterDb);
Check(await waterDb.AlgorithmRules.CountAsync() == waterRuleCount &&
    await waterDb.ResponseTemplates.CountAsync() == waterTemplateCount,
    "restarting the workbook import does not add duplicate rules or answers");

await using var digestDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("daily-digest-" + Guid.NewGuid()).Options);
await digestDb.Database.EnsureCreatedAsync();
var digestVfs = new MonitoredObject { Code = "ВФС", Name = "ВФС" };
var digestNs = new MonitoredObject { Code = "НС1", Name = "НС 1" };
var digestRchv = new MonitoredObject { Code = "РЧВ7", Name = "РЧВ 7" };
var digestChatVfs = new SourceChat { TelegramChatId = -5001, Name = "Алгоритми ВФС" };
var digestChatVdv = new SourceChat { TelegramChatId = -5002, Name = "Алгоритми ВДВП" };
var digestCategory = new ProblemCategory { Name = "Без категорії" };
digestDb.AddRange(digestVfs, digestNs, digestRchv, digestChatVfs, digestChatVdv, digestCategory);
await digestDb.SaveChangesAsync();
var digestVfsRule = new AlgorithmRule { ObjectId = digestVfs.Id, CategoryId = digestCategory.Id, Name = "Низький хлор", MatchPattern = "test" };
var digestNsRule = new AlgorithmRule { ObjectId = digestNs.Id, CategoryId = digestCategory.Id, Name = "Тиск насоса", MatchPattern = "test" };
var digestRchvRule = new AlgorithmRule { ObjectId = digestRchv.Id, CategoryId = digestCategory.Id, Name = "Засувка в ручному режимі", MatchPattern = "test" };
digestDb.AlgorithmRules.AddRange(digestVfsRule, digestNsRule, digestRchvRule);
await digestDb.SaveChangesAsync();
var digestStart = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
var digestMessages = Enumerable.Range(1, 3).Select(i => new IncomingMessage { ChatId = digestChatVfs.Id,
    TelegramMessageId = i, TelegramDateUtc = digestStart, ReceivedAtUtc = digestStart, Text = "test" }).ToArray();
digestDb.IncomingMessages.AddRange(digestMessages);
await digestDb.SaveChangesAsync();
var digestIncidents = new[]
{
    new Incident { ChatId = digestChatVfs.Id, ObjectId = digestVfs.Id, CategoryId = digestCategory.Id,
        AlgorithmRuleId = digestVfsRule.Id, StartMessageId = digestMessages[0].Id, StartedAtUtc = digestStart },
    new Incident { ChatId = digestChatVfs.Id, ObjectId = digestNs.Id, CategoryId = digestCategory.Id,
        AlgorithmRuleId = digestNsRule.Id, StartMessageId = digestMessages[1].Id, StartedAtUtc = digestStart },
    new Incident { ChatId = digestChatVfs.Id, ObjectId = digestRchv.Id, CategoryId = digestCategory.Id,
        AlgorithmRuleId = digestRchvRule.Id, StartMessageId = digestMessages[2].Id, StartedAtUtc = digestStart }
};
digestDb.Incidents.AddRange(digestIncidents);
await digestDb.SaveChangesAsync();
Check(await DailyDigestService.AddInitialSchedulesAsync(digestDb) == 2 &&
    await DailyDigestService.AddInitialSchedulesAsync(digestDb) == 0,
    "two requested chat schedules are initialized once at 16:00 Kyiv time");
var digestBefore = new DateTimeOffset(2026, 10, 9, 12, 59, 0, TimeSpan.Zero);
var digestAt = digestBefore.AddMinutes(1);
Check(await DailyDigestService.QueueDueAsync(digestDb, digestBefore) == 0 &&
    await DailyDigestService.QueueDueAsync(digestDb, digestAt) == 2 &&
    await DailyDigestService.QueueDueAsync(digestDb, digestAt.AddMinutes(5)) == 0,
    "daily digest queues once per chat and Kyiv calendar day");
var digestScheduled = await digestDb.DailyDigestDeliveries.OrderBy(x => x.Id).ToListAsync();
var vfsDigest = digestScheduled.Single(x => x.ScheduleId == digestDb.DailyDigestSchedules.Single(s => s.ChatId == digestChatVfs.Id).Id);
var vdvDigest = digestScheduled.Single(x => x.ScheduleId == digestDb.DailyDigestSchedules.Single(s => s.ChatId == digestChatVdv.Id).Id);
Check(vfsDigest.Text.Contains("Низький хлор") && vfsDigest.Text.Contains("Тиск насоса") &&
    !vfsDigest.Text.Contains("Засувка в ручному режимі") && vdvDigest.Text.Contains("Засувка в ручному режимі") &&
    !vdvDigest.Text.Contains("Низький хлор"),
    "each chat receives only active unanswered algorithms for its selected objects, regardless of source chat");
var digestTelegramHandler = new RecordingTelegramHandler();
var digestTelegram = new TelegramClient(new HttpClient(digestTelegramHandler), telegramConfig);
await DailyDigestService.DeliverDueAsync(digestDb, digestTelegram, false, digestAt,
    NullLogger<NotificationWorker>.Instance);
Check(await digestDb.DailyDigestDeliveries.CountAsync(x => x.Status == "sent") == 2 &&
    digestTelegramHandler.Bodies.Count == 2,
    "daily summaries are sent once to both configured Telegram chats");
digestIncidents[0].EndedAtUtc = digestAt;
digestDb.Responses.Add(new IncidentResponse { IncidentId = digestIncidents[1].Id, UserId = 1,
    Text = "Відповідь", CreatedAtUtc = digestAt });
await digestDb.SaveChangesAsync();
Check(await DailyDigestService.QueueDueAsync(digestDb, digestAt.AddDays(1)) == 2 &&
    (await digestDb.DailyDigestDeliveries.Where(x => x.LocalDate == "2026-10-10" && x.ScheduleId == vfsDigest.ScheduleId)
        .SingleAsync()).Text.Contains("Наразі таких алгоритмів немає"),
    "completed or answered algorithms disappear from the following day's digest");
Check(await DailyDigestService.QueueDueAsync(digestDb, new DateTimeOffset(2026, 11, 9, 13, 59, 0, TimeSpan.Zero)) == 0 &&
    await DailyDigestService.QueueDueAsync(digestDb, new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.Zero)) == 2,
    "Kyiv winter time still sends at local 16:00");

await using (var currentDb = new AlgorithmDbContext(new DbContextOptionsBuilder<AlgorithmDbContext>()
    .UseInMemoryDatabase("current-objects-" + Guid.NewGuid()).Options))
{
    await currentDb.Database.EnsureCreatedAsync();
    var genericRchv = new MonitoredObject { Code = "РЧВ", Name = "РЧВ" };
    var genericPns = new MonitoredObject { Code = "ПНС", Name = "ПНС" };
    var pnsAlias = new MonitoredObject { Code = "ПНС-1", Name = "Стара назва ПНС" };
    currentDb.Objects.AddRange(genericRchv, genericPns, pnsAlias);
    var category = new ProblemCategory { Name = "Без категорії" };
    currentDb.Categories.Add(category);
    var user = new AppUser { TelegramId = 123456, DisplayName = "Черговий", Status = "approved" };
    currentDb.Users.Add(user);
    var chat = new SourceChat { TelegramChatId = -123456, Name = "Алгоритми ВДВП" };
    currentDb.Chats.Add(chat);
    await currentDb.SaveChangesAsync();
    var legacyRule = new AlgorithmRule { ObjectId = genericPns.Id, CategoryId = category.Id,
        Name = "Тиск насоса низький", MatchPattern = "^Тиск насоса низький$" };
    currentDb.AlgorithmRules.Add(legacyRule);
    currentDb.RouteRules.Add(new RouteRule { UserId = user.Id, ObjectId = genericRchv.Id });
    currentDb.UserScopes.Add(new UserScope { UserId = user.Id, ObjectId = genericPns.Id });
    currentDb.DailyDigestSchedules.Add(new DailyDigestSchedule { ChatId = chat.Id, ObjectIdsCsv = genericRchv.Id.ToString() });
    await currentDb.SaveChangesAsync();
    Check(await CurrentObjectsSeed.ApplyAsync(currentDb) == 24 &&
        await CurrentObjectsSeed.ApplyAsync(currentDb) == 0 &&
        await currentDb.Objects.CountAsync(x => x.Enabled) == 25,
        "current object list has exactly 20 RCHV, 3 PNS, VFS and NS without repeated seeding");
    Check(!genericRchv.Enabled && !genericPns.Enabled && pnsAlias.Enabled && pnsAlias.Name == "ПНС-1" &&
        AlgorithmCatalog.Family("РЧВ ІПС") == "РЧВ" && AlgorithmCatalog.Family("ПНС-1") == "ПНС",
        "legacy group placeholders are hidden and spaced or hyphenated object codes share a family");
    Check(legacyRule.ObjectId == pnsAlias.Id &&
        (await AlgorithmCatalog.ResolveAsync(currentDb, "🔴 08:00:00 ПНС 1: Тиск насоса низький" )).Id == legacyRule.Id,
        "existing PNS rule and alias are reused for incoming event spelling variants");
    Check(await currentDb.RouteRules.CountAsync(x => x.UserId == user.Id && x.Enabled) == 20 &&
        await currentDb.UserScopes.CountAsync(x => x.UserId == user.Id) == 23 &&
        DailyDigestService.ObjectIds(currentDb.DailyDigestSchedules.Single().ObjectIdsCsv).Length == 20,
        "legacy RCHV responsibility and digest expand to the group while PNS viewing access is preserved");
    var selectedActive = await DailyDigestService.ActiveObjectIdsAsync(currentDb,
        [genericRchv.Id, pnsAlias.Id, -999, pnsAlias.Id]);
    Check(selectedActive.SequenceEqual([pnsAlias.Id]) &&
        (await DailyDigestService.ActiveObjectIdsAsync(currentDb, [genericRchv.Id])).Length == 0,
        "digest selection drops retired object IDs and keeps selected active objects");
}

sealed class CapturingTelegramHandler : HttpMessageHandler
{
    public string? Body { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.BadRequest);
    }
}

sealed class RecordingTelegramHandler : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];
    private int nextMessageId = 5000;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent($"{{\"ok\":true,\"result\":{{\"message_id\":{++nextMessageId}}}}}") };
    }
}
