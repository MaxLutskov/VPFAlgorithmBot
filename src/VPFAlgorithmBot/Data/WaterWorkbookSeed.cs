using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Data;

public static class WaterWorkbookSeed
{
    private sealed record Entry(string Group, string Name, string Status, string[] Answers);
    private sealed record Report(int Rows, int Matched, int Created, int TemplatesAdded, string[] RenamedMatches, string[] NeedsReview);
    public const string ReportKey = "catalog:water-workbook:report";

    public static Task<string> ApplyAsync(AlgorithmDbContext db, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            using var stream = typeof(WaterWorkbookSeed).Assembly.GetManifestResourceStream("VPFAlgorithmBot.Data.water-algorithms.json")
                ?? throw new InvalidOperationException("Water algorithm catalog is missing from the application.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync(ct);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            const string markerKey = "catalog:water-workbook:hash";
            if (await db.Settings.FindAsync([markerKey], ct) is { Value: var applied } && applied == hash)
                return (await db.Settings.FindAsync([ReportKey], ct))?.Value ?? "{}";

            var entries = JsonSerializer.Deserialize<Entry[]>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var objects = await db.Objects.ToListAsync(ct);
            var rules = await db.AlgorithmRules.ToListAsync(ct);
            var templates = await db.ResponseTemplates.ToListAsync(ct);
            var category = await db.Categories.FirstOrDefaultAsync(x => x.Name == "Без категорії", ct);
            if (category is null)
            {
                category = new ProblemCategory { Name = "Без категорії" };
                db.Categories.Add(category);
                await db.SaveChangesAsync(ct);
            }

            var matched = 0;
            var created = 0;
            var templatesAdded = 0;
            var newTemplates = new List<ResponseTemplate>();
            var review = new List<string>();
            var renamedMatches = new List<string>();
            var desiredAnswers = new Dictionary<int, HashSet<string>>();
            foreach (var entry in entries)
            {
                var family = entry.Group switch { "ВДВП" => "РЧВ", "НС1" => "НС", _ => entry.Group };
                var familyObjects = objects.Where(x => AlgorithmCatalog.Family(x.Code) == family).ToArray();
                if (familyObjects.Length == 0)
                {
                    var obj = new MonitoredObject { Code = family, Name = family };
                    db.Objects.Add(obj);
                    await db.SaveChangesAsync(ct);
                    objects.Add(obj);
                    familyObjects = [obj];
                }
                var familyRules = rules.Where(x => familyObjects.Any(obj => obj.Id == x.ObjectId)).ToArray();
                var canonical = AlgorithmCatalog.CanonicalIds(familyRules, objects);
                var existing = AlgorithmNameMatcher.Find(entry.Name, familyRules.Where(x => canonical[x.Id] == x.Id));
                if (existing is null && familyRules.Count(x => AlgorithmNameMatcher.Key(x.Name) == AlgorithmNameMatcher.Key(entry.Name)) > 1)
                {
                    review.Add($"{entry.Group}: {entry.Name}");
                    continue;
                }
                AlgorithmRule rule;
                if (existing is not null)
                {
                    rule = existing;
                    matched++;
                    if (!AlgorithmCatalog.RuleName(entry.Name).Equals(AlgorithmCatalog.RuleName(rule.Name), StringComparison.OrdinalIgnoreCase))
                        renamedMatches.Add($"{entry.Group}: {entry.Name} => {rule.Name}");
                }
                else
                {
                    var name = AlgorithmCatalog.RuleName(entry.Name);
                    rule = new AlgorithmRule
                    {
                        ObjectId = familyObjects.OrderBy(x => x.Id).First().Id, CategoryId = category.Id,
                        Name = name, MatchPattern = "^" + Regex.Escape(name) + "$",
                        Enabled = !entry.Status.Contains("вимкнено", StringComparison.OrdinalIgnoreCase) &&
                                  !entry.Status.Contains("відключено", StringComparison.OrdinalIgnoreCase) &&
                                  !entry.Status.Contains("Немає доступу", StringComparison.OrdinalIgnoreCase)
                    };
                    db.AlgorithmRules.Add(rule);
                    await db.SaveChangesAsync(ct);
                    rules.Add(rule);
                    created++;
                }

                var forRule = templates.Where(x => x.AlgorithmRuleId == rule.Id).ToArray();
                if (!desiredAnswers.TryGetValue(rule.Id, out var desired))
                    desiredAnswers[rule.Id] = desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var answer in entry.Answers.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    desired.Add(AlgorithmCatalog.RuleName(answer));
                    if (forRule.FirstOrDefault(x => SameText(x.Text, answer)) is { } present)
                    {
                        present.Enabled = true;
                        continue;
                    }
                    var title = answer.Length <= 80 ? answer : answer[..77].TrimEnd() + "…";
                    var template = new ResponseTemplate
                    {
                        AlgorithmRuleId = rule.Id, CategoryId = rule.CategoryId,
                        Title = title, Text = answer, SortOrder = 100 + templatesAdded * 10
                    };
                    db.ResponseTemplates.Add(template);
                    templates.Add(template);
                    newTemplates.Add(template);
                    templatesAdded++;
                }
            }
            foreach (var template in templates.Where(x => x.AlgorithmRuleId is not null && desiredAnswers.ContainsKey(x.AlgorithmRuleId.Value)))
                template.Enabled = desiredAnswers[template.AlgorithmRuleId!.Value].Contains(AlgorithmCatalog.RuleName(template.Text));
            await db.SaveChangesAsync(ct);
            db.TemplateVersions.AddRange(newTemplates.Select(template => new TemplateVersion
            {
                TemplateId = template.Id, Version = 1, Title = template.Title,
                Text = template.Text, SavedAtUtc = DateTimeOffset.UtcNow
            }));
            var report = JsonSerializer.Serialize(new Report(entries.Length, matched, created, templatesAdded, renamedMatches.ToArray(), review.ToArray()));
            var marker = await db.Settings.FindAsync([markerKey], ct);
            if (marker is null) db.Settings.Add(new Setting { Key = markerKey, Value = hash });
            else marker.Value = hash;
            var reportSetting = await db.Settings.FindAsync([ReportKey], ct);
            if (reportSetting is null) db.Settings.Add(new Setting { Key = ReportKey, Value = report });
            else reportSetting.Value = report;
            await db.SaveChangesAsync(ct);
            return report;
        }, ct);

    private static bool SameText(string left, string right) =>
        AlgorithmCatalog.RuleName(left).Equals(AlgorithmCatalog.RuleName(right), StringComparison.OrdinalIgnoreCase);

}
