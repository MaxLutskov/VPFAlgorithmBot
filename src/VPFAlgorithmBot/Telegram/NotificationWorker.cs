using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Telegram;

public sealed class NotificationWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AlgorithmDbContext>();
                var telegram = scope.ServiceProvider.GetRequiredService<TelegramClient>();
                await ScheduleRemindersAsync(db, stoppingToken);
                var due = await db.NotificationOutbox.Where(x => x.Status == "pending" && x.DueAtUtc <= DateTimeOffset.UtcNow)
                    .OrderBy(x => x.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var item in due)
                {
                    if (item.Kind.StartsWith("reminder:") && await db.Responses.AnyAsync(x => x.IncidentId == item.IncidentId, stoppingToken))
                    {
                        item.Status = "cancelled";
                        continue;
                    }
                    var user = await db.Users.FindAsync([item.UserId], stoppingToken);
                    if (user is null || user.Status != "approved")
                    {
                        item.Status = "failed"; item.LastError = "Одержувач недоступний"; continue;
                    }
                    try
                    {
                        if (config["Telegram:Mode"] == "Demo")
                        {
                            item.Status = "sent"; item.SentAtUtc = DateTimeOffset.UtcNow;
                        }
                        else
                        {
                            var markup = new { inline_keyboard = new[] { new[] { new { text = "Надати відповідь", callback_data = $"templates:{item.IncidentId}" } } } };
                            await telegram.SendAsync(user.TelegramId, item.Text, markup, stoppingToken);
                            item.Status = "sent"; item.SentAtUtc = DateTimeOffset.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Attempts++;
                        item.LastError = ex.Message[..Math.Min(300, ex.Message.Length)];
                        item.Status = item.Attempts >= 8 ? "failed" : "pending";
                        item.DueAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, item.Attempts)));
                        logger.LogWarning(ex, "Помилка доставки {OutboxId}", item.Id);
                    }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) { logger.LogError(ex, "Помилка обробки outbox"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ScheduleRemindersAsync(AlgorithmDbContext db, CancellationToken ct)
    {
        var overrideSetting = await db.Settings.FindAsync(["reminder_minutes"], ct);
        if (!int.TryParse(overrideSetting?.Value ?? config["Reminder:Minutes"], out var minutes) || minutes <= 0) return;
        var now = DateTimeOffset.UtcNow;
        var threshold = now.AddMinutes(-minutes);
        var incidents = await db.Incidents.Where(x => x.StartedAtUtc <= threshold && !db.Responses.Any(r => r.IncidentId == x.Id)).ToListAsync(ct);
        foreach (var incident in incidents)
        {
            var initial = await db.NotificationOutbox.Where(x => x.IncidentId == incident.Id && x.Kind == "problem")
                .Select(x => x.UserId).Distinct().ToListAsync(ct);
            foreach (var userId in initial)
            {
                var latest = await db.NotificationOutbox.Where(x => x.IncidentId == incident.Id && x.UserId == userId && x.Kind.StartsWith("reminder:"))
                    .OrderByDescending(x => x.DueAtUtc).Select(x => (DateTimeOffset?)x.DueAtUtc).FirstOrDefaultAsync(ct);
                if (latest is not null && latest > threshold) continue;
                var slot = now.ToUnixTimeSeconds() / Math.Max(60, minutes * 60);
                var kind = $"reminder:{slot}";
                if (await db.NotificationOutbox.AnyAsync(x => x.IncidentId == incident.Id && x.UserId == userId && x.Kind == kind, ct)) continue;
                var obj = await db.Objects.FindAsync([incident.ObjectId], ct);
                var rule = await db.AlgorithmRules.FindAsync([incident.AlgorithmRuleId], ct);
                db.NotificationOutbox.Add(new NotificationOutbox
                {
                    IncidentId = incident.Id, UserId = userId, Kind = kind,
                    Text = $"⏰ {obj?.Name} — {rule?.Name}: досі без відповіді. Відповідь можна надати і після завершення.",
                    DueAtUtc = now
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class TelegramPollingWorker(IServiceScopeFactory scopes, TelegramClient telegram, IConfiguration config, ILogger<TelegramPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (config["Telegram:Mode"] != "Polling") return;
        if (!telegram.Configured) throw new InvalidOperationException("Не налаштовано Telegram:BotToken");
        await telegram.DeleteWebhookAsync(token);
        long offset = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var updates = await telegram.GetUpdatesAsync(offset, token);
                foreach (var update in updates)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<BotUpdateService>().HandleAsync(update, token);
                        offset = update.UpdateId + 1;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Update {UpdateId} не оброблено; повторимо його", update.UpdateId);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Polling error"); await Task.Delay(TimeSpan.FromSeconds(3), token); }
        }
    }
}
