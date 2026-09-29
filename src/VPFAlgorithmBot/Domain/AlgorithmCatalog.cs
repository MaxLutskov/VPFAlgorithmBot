using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Domain;

public static class AlgorithmCatalog
{
    // Call inside DatabaseWork to make discovery idempotent across simultaneous deliveries.
    public static async Task<AlgorithmRule> ResolveAsync(AlgorithmDbContext db, string text, CancellationToken ct = default, bool includeDisabled = false)
    {
        var description = EventParser.Describe(text);
        var objects = await db.Objects.ToListAsync(ct);
        var obj = objects.SingleOrDefault(x => EventParser.NormalizeObjectCode(x.Code) == description.ObjectCode);
        if (obj is { Enabled: false } && !includeDisabled) throw new FormatException("Об’єкт вимкнений адміністратором.");
        if (obj is null)
        {
            obj = new MonitoredObject { Code = description.ObjectCode, Name = description.ObjectName };
            db.Objects.Add(obj);
            await db.SaveChangesAsync(ct);
        }
        var rules = await db.AlgorithmRules.Where(x => x.ObjectId == obj.Id).ToListAsync(ct);
        var matches = rules.Where(x => EventParser.Matches(x, text)).ToList();
        if (matches.Any())
        {
            if (includeDisabled)
            {
                var ordered = matches.OrderBy(x => x.Priority).ThenBy(x => x.Id).ToArray();
                if (ordered.Length > 1 && ordered[0].Priority == ordered[1].Priority)
                    throw new FormatException("Кілька правил мають однаковий пріоритет.");
                return ordered[0];
            }
            if (!matches.Any(x => x.Enabled)) throw new FormatException("Алгоритм вимкнений адміністратором.");
            return EventParser.MatchRule(matches, text);
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
            Name = description.Name, MatchPattern = description.MatchPattern
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
            AlgorithmRuleId = rule.Id, ObjectId = rule.ObjectId, CategoryId = rule.CategoryId,
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
