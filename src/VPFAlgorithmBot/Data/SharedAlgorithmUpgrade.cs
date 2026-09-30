using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Data;

public static class SharedAlgorithmUpgrade
{
    public const string Marker = "catalog:shared-algorithms:v1";

    public static Task<int> ApplyAsync(AlgorithmDbContext db, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            if (await db.Settings.AnyAsync(x => x.Key == Marker, ct)) return 0;
            var objects = await db.Objects.ToListAsync(ct);
            var rules = await db.AlgorithmRules.ToListAsync(ct);
            var canonical = AlgorithmCatalog.CanonicalIds(rules, objects);
            var aliases = canonical.Where(x => x.Key != x.Value).ToArray();
            if (aliases.Length > 0)
            {
                foreach (var group in rules.GroupBy(x => canonical[x.Id]).Where(x => x.Count() > 1))
                {
                    var representative = group.Single(x => x.Id == group.Key);
                    var preferred = group.Where(x => x.Enabled).OrderBy(x => x.Id).FirstOrDefault();
                    if (preferred is not null && preferred.Id != representative.Id)
                    {
                        representative.CategoryId = preferred.CategoryId;
                        representative.MatchPattern = preferred.MatchPattern;
                        representative.Priority = preferred.Priority;
                        representative.Enabled = true;
                    }
                }
                var incidents = await db.Incidents.ToListAsync(ct);
                foreach (var incident in incidents)
                    if (canonical.TryGetValue(incident.AlgorithmRuleId, out var ruleId)) incident.AlgorithmRuleId = ruleId;

                var templates = await db.ResponseTemplates.ToListAsync(ct);
                foreach (var group in templates.Where(x => x.AlgorithmRuleId is not null && canonical.ContainsKey(x.AlgorithmRuleId.Value))
                    .GroupBy(x => (RuleId: canonical[x.AlgorithmRuleId!.Value], Title: x.Title.Trim().ToUpperInvariant())))
                {
                    var winner = group.OrderByDescending(x => x.Version)
                        .ThenBy(x => x.AlgorithmRuleId == group.Key.RuleId ? 0 : 1)
                        .ThenBy(x => x.Id).First();
                    winner.AlgorithmRuleId = group.Key.RuleId;
                    winner.ObjectId = null;
                    foreach (var duplicate in group.Where(x => x.Id != winner.Id)) duplicate.Enabled = false;
                }
                foreach (var rule in rules.Where(x => canonical[x.Id] != x.Id)) rule.Enabled = false;
                await db.SaveChangesAsync(ct);
            }
            db.Settings.Add(new Setting { Key = Marker, Value = aliases.Length.ToString() });
            db.AuditEvents.Add(new AuditEvent { Action = "consolidate-algorithms", Entity = "AlgorithmRule",
                Detail = $"Merged {aliases.Length} aliases into shared family types", CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            return aliases.Length;
        }, ct);
}
