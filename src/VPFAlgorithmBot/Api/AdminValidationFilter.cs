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
                if (!await db.Objects.AnyAsync(x => x.Id == rule.ObjectId, ct) || !await db.Categories.AnyAsync(x => x.Id == rule.CategoryId, ct)) return "Оберіть наявний об’єкт та категорію.";
                try { _ = new Regex(rule.MatchPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { return "Некоректне правило розпізнавання."; }
                if (await db.AlgorithmRules.AnyAsync(x => x.Id != id && x.ObjectId == rule.ObjectId && x.MatchPattern == rule.MatchPattern, ct)) return "Таке правило для цього об’єкта вже існує.";
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
                break;
            case UserScope scope:
                if (!await db.Users.AnyAsync(x => x.Id == scope.UserId, ct) || !await db.Objects.AnyAsync(x => x.Id == scope.ObjectId, ct)) return "Оберіть наявного користувача та об’єкт.";
                break;
        }
        return null;
    }
}
