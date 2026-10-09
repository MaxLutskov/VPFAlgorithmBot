using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Domain;

public static class AlgorithmCatalog
{
    public static string Family(string code)
    {
        var normalized = EventParser.NormalizeObjectCode(code);
        if (normalized == "РЧВІПС") return "РЧВ";
        var family = Regex.Replace(normalized, @"\d+$", "");
        return family.Length == 0 ? normalized : family;
    }

    public static string RuleName(string name) => EventParser.NormalizeText(name).TrimEnd('.').Trim();

    public static IReadOnlyDictionary<int, int> CanonicalIds(IEnumerable<AlgorithmRule> rules, IEnumerable<MonitoredObject> objects)
    {
        var families = objects.ToDictionary(x => x.Id, x => Family(x.Code));
        return rules.Where(x => families.ContainsKey(x.ObjectId))
            .GroupBy(x => (families[x.ObjectId], RuleName(x.Name)), StringTupleComparer.Instance)
            .SelectMany(group =>
            {
                var representative = group.OrderBy(x => x.Id).First();
                return group.Select(rule => (AliasId: rule.Id, CanonicalId: representative.Id));
            }).ToDictionary(x => x.AliasId, x => x.CanonicalId);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Family, string Name)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string Family, string Name) x, (string Family, string Name) y) =>
            string.Equals(x.Family, y.Family, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Family, string Name) value) =>
            HashCode.Combine(value.Family.ToUpperInvariant(), value.Name.ToUpperInvariant());
    }

    public static async Task<IReadOnlyDictionary<int, int>> CanonicalIdsAsync(AlgorithmDbContext db, CancellationToken ct) =>
        CanonicalIds(await db.AlgorithmRules.AsNoTracking().ToListAsync(ct), await db.Objects.AsNoTracking().ToListAsync(ct));

    public static async Task<AlgorithmRule> MatchExistingAsync(AlgorithmDbContext db, string text, CancellationToken ct = default)
    {
        var description = EventParser.Describe(text);
        var objects = await db.Objects.AsNoTracking().ToListAsync(ct);
        var obj = objects.Where(x => EventParser.NormalizeObjectCode(x.Code) == description.ObjectCode)
            .OrderByDescending(x => x.Enabled).ThenBy(x => x.Id).FirstOrDefault()
            ?? throw new FormatException("Об’єкт ще не додано до довідника.");
        var familyIds = objects.Where(x => Family(x.Code) == Family(obj.Code)).Select(x => x.Id).ToHashSet();
        var rules = (await db.AlgorithmRules.AsNoTracking().ToListAsync(ct)).Where(x => familyIds.Contains(x.ObjectId)).ToList();
        var map = CanonicalIds(rules, objects);
        var matches = rules.Where(x => RuleName(x.Name).Equals(description.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) matches = rules.Where(x => EventParser.Matches(x, text)).ToArray();
        if (matches.Length == 0 && AlgorithmNameMatcher.Find(description.Name, rules.Where(x => map[x.Id] == x.Id)) is { } close)
            matches = [close];
        var canonicalIds = matches.Select(x => map[x.Id]).Distinct().ToHashSet();
        var candidates = rules.Where(x => canonicalIds.Contains(x.Id) && x.Enabled).OrderBy(x => x.Priority).ThenBy(x => x.Id).ToArray();
        if (candidates.Length == 0) throw new FormatException("Немає правила розпізнавання.");
        if (candidates.Length > 1 && candidates[0].Priority == candidates[1].Priority)
            throw new FormatException("Кілька правил мають однаковий пріоритет.");
        return candidates[0];
    }

    // Call inside DatabaseWork to make discovery idempotent across simultaneous deliveries.
    public static async Task<AlgorithmRule> ResolveAsync(AlgorithmDbContext db, string text, CancellationToken ct = default, bool includeDisabled = false)
    {
        var description = EventParser.Describe(text);
        var objects = await db.Objects.ToListAsync(ct);
        var obj = objects.Where(x => EventParser.NormalizeObjectCode(x.Code) == description.ObjectCode)
            .OrderByDescending(x => x.Enabled).ThenBy(x => x.Id).FirstOrDefault();
        if (obj is { Enabled: false } && !includeDisabled) throw new FormatException("Об’єкт вимкнений адміністратором.");
        if (obj is null)
        {
            obj = new MonitoredObject { Code = description.ObjectCode, Name = description.ObjectName };
            db.Objects.Add(obj);
            await db.SaveChangesAsync(ct);
            objects.Add(obj);
        }
        var relatedObjectIds = objects.Where(x => Family(x.Code) == Family(obj.Code)).Select(x => x.Id).ToArray();
        var rules = await db.AlgorithmRules.Where(x => relatedObjectIds.Contains(x.ObjectId)).ToListAsync(ct);
        var canonical = CanonicalIds(rules, objects);
        var sameName = rules.Where(x => RuleName(x.Name).Equals(description.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = sameName.Count > 0 ? sameName : rules.Where(x => EventParser.Matches(x, text)).ToList();
        if (matches.Count == 0 && AlgorithmNameMatcher.Find(description.Name, rules.Where(x => canonical[x.Id] == x.Id)) is { } close)
            matches.Add(close);
        if (matches.Any())
        {
            var distinct = matches.Select(x => canonical[x.Id]).Distinct().ToArray();
            var candidates = rules.Where(x => distinct.Contains(x.Id)).ToArray();
            if (candidates.Length > 1 && candidates.OrderBy(x => x.Priority).Take(2).Select(x => x.Priority).Distinct().Count() == 1)
                throw new FormatException("Кілька правил мають однаковий пріоритет.");
            var chosen = candidates.OrderBy(x => x.Priority).ThenBy(x => x.Id).First();
            if (!includeDisabled && !chosen.Enabled)
                throw new FormatException("Алгоритм вимкнений адміністратором.");
            return chosen;
        }
        const string defaultCategory = "Без категорії";
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Name == defaultCategory, ct);
        if (category is null)
        {
            category = new ProblemCategory { Name = defaultCategory };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
        }
        var rule = new AlgorithmRule
        {
            ObjectId = obj.Id, CategoryId = category.Id,
            Name = description.Name, MatchPattern = "^" + Regex.Escape(description.Name) + "$"
        };
        db.AlgorithmRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await AddTestTemplatesAsync(db, rule, ct);
        return rule;
    }

    public static async Task AddTestTemplatesAsync(AlgorithmDbContext db, AlgorithmRule rule, CancellationToken ct = default)
    {
        if (await db.ResponseTemplates.AnyAsync(x => x.AlgorithmRuleId == rule.Id, ct)) return;
        var choices = new[]
        {
            ("Показники перевірено", "Перевірено показники та стан обладнання."),
            ("Повідомлено відповідального", "Про відхилення повідомлено відповідального спеціаліста."),
            ("Виконано коригування", "Виконано коригування налаштувань обладнання."),
            ("Втручання не знадобилося", "Показники нормалізувалися без втручання оператора.")
        };
        var templates = choices.Select((choice, index) => new ResponseTemplate
        {
            AlgorithmRuleId = rule.Id, CategoryId = rule.CategoryId,
            Title = choice.Item1, Text = choice.Item2, SortOrder = (index + 1) * 10
        }).ToArray();
        db.ResponseTemplates.AddRange(templates);
        await db.SaveChangesAsync(ct);
        db.TemplateVersions.AddRange(templates.Select(template => new TemplateVersion
        {
            TemplateId = template.Id, Version = 1, Title = template.Title,
            Text = template.Text, SavedAtUtc = DateTimeOffset.UtcNow
        }));
        await db.SaveChangesAsync(ct);
    }
}
