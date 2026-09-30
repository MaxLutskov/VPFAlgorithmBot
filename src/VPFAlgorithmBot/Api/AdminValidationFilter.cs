using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Api;

public sealed class AdminValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var data = context.Arguments.FirstOrDefault(x => x is SourceChat or MonitoredObject or ProblemCategory or AlgorithmRule or RouteRule or ResponseTemplate or UserScope);
        if (data is null) return await next(context);
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AlgorithmDbContext>();
        var ct = context.HttpContext.RequestAborted;
        if (!await MiniAppEndpoints.IsAdmin(context.HttpContext.Request, services.GetRequiredService<MiniAppAuth>(),
            services.GetRequiredService<AdminSession>(), db, ct)) return Results.StatusCode(403);
        int.TryParse(context.HttpContext.Request.RouteValues["id"]?.ToString(), out var id);
        try
        {
            return await DatabaseWork.RunAsync<object?>(db, async () =>
            {
                var error = await ValidateAsync(db, data, id, ct);
                return error is null ? await next(context) : Results.BadRequest(error);
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Results.Conflict("Запис із таким ідентифікатором уже існує. Оновіть довідник.");
        }
    }

    private static bool Required(string? value, int length) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= length;

    private static async Task<string?> ValidateAsync(AlgorithmDbContext db, object data, int id, CancellationToken ct)
    {
        switch (data)
        {
            case SourceChat chat:
                if (!Required(chat.Name, 200) || chat.TelegramChatId == 0) return "Задайте назву (до 200 символів) та ненульовий Telegram ID чату.";
                chat.Name = chat.Name.Trim();
                if (await db.Chats.AnyAsync(x => x.Id != id && x.TelegramChatId == chat.TelegramChatId, ct)) return "Чат із таким Telegram ID уже існує.";
                break;
            case MonitoredObject obj:
                if (!Required(obj.Code, 50) || !Required(obj.Name, 200)) return "Задайте код (до 50 символів) та назву об’єкта (до 200 символів).";
                obj.Code = EventParser.NormalizeObjectCode(obj.Code);
                if (id != 0)
                {
                    var oldObject = await db.Objects.FindAsync([id], ct);
                    if (oldObject is not null && AlgorithmCatalog.Family(oldObject.Code) != AlgorithmCatalog.Family(obj.Code) &&
                        (await db.AlgorithmRules.AnyAsync(x => x.ObjectId == id, ct) || await db.Incidents.AnyAsync(x => x.ObjectId == id, ct)))
                        return "Об’єкт з історією не можна переносити до іншої групи. Змініть назву або створіть новий об’єкт.";
                }
                var codes = await db.Objects.Where(x => x.Id != id).Select(x => x.Code).ToListAsync(ct);
                if (codes.Any(x => EventParser.NormalizeObjectCode(x) == obj.Code)) return "Об’єкт із таким кодом уже існує.";
                break;
            case ProblemCategory category:
                if (!Required(category.Name, 200)) return "Задайте назву категорії (до 200 символів).";
                category.Name = category.Name.Trim();
                if (await db.Categories.AnyAsync(x => x.Id != id && x.Name == category.Name, ct)) return "Категорія з такою назвою вже існує.";
                break;
            case AlgorithmRule rule:
                if (!Required(rule.Name, 300) || !Required(rule.MatchPattern, 500)) return "Задайте назву (до 300 символів) та правило розпізнавання (до 500 символів).";
                if (rule.CategoryId == 0)
                {
                    var defaultCategory = await db.Categories.SingleOrDefaultAsync(x => x.Name == "Без категорії", ct);
                    if (defaultCategory is null)
                    {
                        defaultCategory = new ProblemCategory { Name = "Без категорії" };
                        db.Categories.Add(defaultCategory);
                        await db.SaveChangesAsync(ct);
                    }
                    rule.CategoryId = defaultCategory.Id;
                }
                if (!await db.Objects.AnyAsync(x => x.Id == rule.ObjectId, ct) || !await db.Categories.AnyAsync(x => x.Id == rule.CategoryId, ct)) return "Оберіть наявний об’єкт та категорію.";
                var selected = await db.Objects.FindAsync([rule.ObjectId], ct);
                if (id != 0)
                {
                    var existing = await db.AlgorithmRules.FindAsync([id], ct);
                    var existingObject = existing is null ? null : await db.Objects.FindAsync([existing.ObjectId], ct);
                    if (existingObject is not null && existing!.ObjectId != rule.ObjectId)
                        return "Групу алгоритму після створення не змінюють. Створіть окремий тип.";
                }
                try { _ = new Regex(rule.MatchPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { return "Некоректне правило розпізнавання."; }
                var allRules = await db.AlgorithmRules.ToListAsync(ct);
                var allObjects = await db.Objects.ToListAsync(ct);
                var canonicalRules = AlgorithmCatalog.CanonicalIds(allRules, allObjects);
                var family = AlgorithmCatalog.Family(selected!.Code);
                if (allRules.Any(x => x.Id != id && canonicalRules[x.Id] == x.Id &&
                    AlgorithmCatalog.Family(allObjects.First(o => o.Id == x.ObjectId).Code) == family &&
                    (AlgorithmCatalog.RuleName(x.Name).Equals(AlgorithmCatalog.RuleName(rule.Name), StringComparison.OrdinalIgnoreCase) ||
                     x.MatchPattern == rule.MatchPattern))) return "Такий алгоритм у цій групі вже існує.";
                break;
            case RouteRule route:
                if (!await db.Users.AnyAsync(x => x.Id == route.UserId, ct)) return "Оберіть наявного користувача.";
                if (route.ChatId is not null && !await db.Chats.AnyAsync(x => x.Id == route.ChatId, ct)) return "Чат не знайдено.";
                if (route.ObjectId is not null && !await db.Objects.AnyAsync(x => x.Id == route.ObjectId, ct)) return "Об’єкт не знайдено.";
                if (route.CategoryId is not null && !await db.Categories.AnyAsync(x => x.Id == route.CategoryId, ct)) return "Категорію не знайдено.";
                break;
            case ResponseTemplate template:
                if (!Required(template.Title, 200) || !Required(template.Text, 2000)) return "Задайте назву (до 200 символів) та текст відповіді (до 2000 символів).";
                if (template.AlgorithmRuleId is null || !await db.AlgorithmRules.AnyAsync(x => x.Id == template.AlgorithmRuleId, ct)) return "Оберіть наявний алгоритм.";
                var map = await AlgorithmCatalog.CanonicalIdsAsync(db, ct);
                if (!map.TryGetValue(template.AlgorithmRuleId.Value, out var canonicalId) || canonicalId != template.AlgorithmRuleId.Value)
                    return "Оберіть спільний тип алгоритму з довідника.";
                break;
            case UserScope scope:
                if (!await db.Users.AnyAsync(x => x.Id == scope.UserId, ct) || !await db.Objects.AnyAsync(x => x.Id == scope.ObjectId, ct)) return "Оберіть наявного користувача та об’єкт.";
                break;
        }
        return null;
    }
}
