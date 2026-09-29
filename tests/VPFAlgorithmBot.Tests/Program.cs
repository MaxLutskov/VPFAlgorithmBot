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
