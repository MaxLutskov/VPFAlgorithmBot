using Microsoft.EntityFrameworkCore;

namespace VPFAlgorithmBot.Data;

public static class DemoSeeder
{
    public static async Task SeedAsync(AlgorithmDbContext db, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return;
        var vfs = new MonitoredObject { Code = "ВФС", Name = "Водофільтрувальна станція" };
        var rchv1 = new MonitoredObject { Code = "РЧВ1", Name = "Резервуар чистої води 1" };
        var rchv2 = new MonitoredObject { Code = "РЧВ2", Name = "Резервуар чистої води 2" };
        var pump = new MonitoredObject { Code = "ПНС", Name = "Підвищувальна насосна станція" };
        var chemistry = new ProblemCategory { Name = "Якість води" };
        var flow = new ProblemCategory { Name = "Витрата води" };
        db.AddRange(vfs, rchv1, rchv2, pump, chemistry, flow);
        await db.SaveChangesAsync(ct);
        var admin = new AppUser { TelegramId = 900001, DisplayName = "Тестовий адміністратор", Role = "admin", Status = "approved" };
        var operatorUser = new AppUser { TelegramId = 900002, DisplayName = "Оператор ВФС", Role = "operator", Status = "approved" };
        db.AddRange(admin, operatorUser);
        await db.SaveChangesAsync(ct);
        db.UserScopes.Add(new UserScope { UserId = operatorUser.Id, ObjectId = vfs.Id });
        var chat = new SourceChat { TelegramChatId = -1001, SenderTelegramId = 77, Name = "Алгоритми ВФС" };
        db.Chats.Add(chat);
        var chlorine = new AlgorithmRule
        {
            ObjectId = vfs.Id, CategoryId = chemistry.Id, Name = "Низька концентрація хлору",
            MatchPattern = @"ВФС:\s*Концентрація залишкового хлору менше"
        };
        var consumption = new AlgorithmRule
        {
            ObjectId = vfs.Id, CategoryId = flow.Id, Name = "Висока витрата Тульчинського напрямку",
            MatchPattern = @"ВФС:\s*Тульчинський напрямок споживає більше"
        };
        db.AlgorithmRules.AddRange(chlorine, consumption);
        db.RouteRules.Add(new RouteRule { ChatId = null, ObjectId = vfs.Id, UserId = operatorUser.Id });
        await db.SaveChangesAsync(ct);
        var templates = new[]
        {
            new ResponseTemplate { Title = "Перевіряю показники", Text = "Проводиться перевірка показників та обладнання.", AlgorithmRuleId = chlorine.Id, ObjectId = vfs.Id, CategoryId = chemistry.Id },
            new ResponseTemplate { Title = "Повідомлено технолога", Text = "Про відхилення повідомлено технолога.", AlgorithmRuleId = consumption.Id, ObjectId = vfs.Id, CategoryId = flow.Id, SortOrder = 110 }
        };
        db.ResponseTemplates.AddRange(templates);
        await db.SaveChangesAsync(ct);
        db.TemplateVersions.AddRange(templates.Select(template => new TemplateVersion
        {
            TemplateId = template.Id, Version = template.Version, Title = template.Title,
            Text = template.Text, SavedAtUtc = DateTimeOffset.UtcNow
        }));
        await db.SaveChangesAsync(ct);
    }
}
