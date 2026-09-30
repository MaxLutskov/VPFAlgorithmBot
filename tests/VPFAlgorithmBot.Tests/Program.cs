using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Api;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;
using VPFAlgorithmBot.Telegram;

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
