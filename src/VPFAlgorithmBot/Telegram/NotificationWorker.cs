using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Telegram;

public sealed class NotificationWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset lastDigestCheck = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AlgorithmDbContext>();
                var telegram = scope.ServiceProvider.GetRequiredService<TelegramClient>();
                await QueueInitialChatInstructionsAsync(db, stoppingToken);
                await DeliverChatInstructionsAsync(db, telegram, stoppingToken);
                await ScheduleRemindersAsync(db, stoppingToken);
                if (DateTimeOffset.UtcNow - lastDigestCheck >= TimeSpan.FromMinutes(1))
                {
                    await DailyDigestService.QueueDueAsync(db, DateTimeOffset.UtcNow, stoppingToken);
                    lastDigestCheck = DateTimeOffset.UtcNow;
                }
                await DailyDigestService.DeliverDueAsync(db, telegram, config["Telegram:Mode"] == "Demo",
                    DateTimeOffset.UtcNow, logger, stoppingToken);
                var due = await db.NotificationOutbox.Where(x => x.Status == "pending" && x.DueAtUtc <= DateTimeOffset.UtcNow)
                    .OrderBy(x => x.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var item in due)
                {
                    if (item.Kind == "chat_prompt" || item.Kind.StartsWith("response_receipt:", StringComparison.Ordinal))
                    {
                        try
                        {
                            var incident = await db.Incidents.FindAsync([item.IncidentId], stoppingToken);
                            var chat = incident is null ? null : await db.Chats.FindAsync([incident.ChatId], stoppingToken);
                            if (chat is null || !chat.Enabled)
                            {
                                item.Status = "failed"; item.LastError = "Чат недоступний"; continue;
                            }
                            if (config["Telegram:Mode"] != "Demo" && item.Kind == "chat_prompt")
                            {
                                var obj = await db.Objects.FindAsync([incident!.ObjectId], stoppingToken);
                                var rule = await db.AlgorithmRules.FindAsync([incident.AlgorithmRuleId], stoppingToken);
                                var templates = await db.ResponseTemplates.AsNoTracking()
                                    .Where(x => x.Enabled && x.AlgorithmRuleId == incident.AlgorithmRuleId)
                                    .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Take(30).ToListAsync(stoppingToken);
                                await telegram.SendAsync(chat.TelegramChatId,
                                    $"🔴 {obj?.Name} — {rule?.Name}\nОберіть готову відповідь або «Своя відповідь»:",
                                    ResponseKeyboard.Build(incident.Id, templates), stoppingToken);
                            }
                            else if (config["Telegram:Mode"] != "Demo")
                            {
                                var source = await db.IncomingMessages.FindAsync([incident!.StartMessageId], stoppingToken);
                                if (source is null) throw new InvalidOperationException("Початкове повідомлення алгоритму не знайдено");
                                await telegram.SendReplyAsync(chat.TelegramChatId, source.TelegramMessageId, item.Text, stoppingToken);
                            }
                            item.Status = "sent"; item.SentAtUtc = DateTimeOffset.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            item.Attempts++;
                            item.LastError = ex.Message[..Math.Min(300, ex.Message.Length)];
                            item.Status = item.Attempts >= 8 ? "failed" : "pending";
                            item.DueAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, item.Attempts)));
                            logger.LogWarning(ex, "Помилка доставки повідомлення у чат {OutboxId}", item.Id);
                        }
                        continue;
                    }
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

    private static async Task QueueInitialChatInstructionsAsync(AlgorithmDbContext db, CancellationToken ct)
    {
        var chatIds = await db.Chats.AsNoTracking().Where(x => x.Enabled).Select(x => x.Id).ToListAsync(ct);
        var queuedIds = await db.ChatInstructionOutbox.AsNoTracking().Where(x => x.Kind == "initial")
            .Select(x => x.ChatId).ToListAsync(ct);
        foreach (var chatId in chatIds.Except(queuedIds))
            db.ChatInstructionOutbox.Add(new ChatInstructionOutbox
            {
                ChatId = chatId, Kind = "initial", EventMessageId = 0, DueAtUtc = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync(ct);
    }

    private async Task DeliverChatInstructionsAsync(AlgorithmDbContext db, TelegramClient telegram, CancellationToken ct)
    {
        var due = await db.ChatInstructionOutbox.Where(x => x.Status == "pending" && x.DueAtUtc <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.Id).Take(20).ToListAsync(ct);
        foreach (var item in due)
        {
            var chat = await db.Chats.FindAsync([item.ChatId], ct);
            if (chat is null || !chat.Enabled)
            {
                item.Status = "failed"; item.LastError = "Чат недоступний"; continue;
            }
            try
            {
                if (config["Telegram:Mode"] != "Demo")
                    await telegram.SendAsync(chat.TelegramChatId, ChatInstructions.Text, null, ct);
                item.Status = "sent";
                item.SentAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                item.Attempts++;
                item.LastError = ex.Message[..Math.Min(300, ex.Message.Length)];
                item.Status = item.Attempts >= 8 ? "failed" : "pending";
                item.DueAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, item.Attempts)));
                logger.LogWarning(ex, "Помилка доставки інструкції в чат {ChatId}", item.ChatId);
            }
        }
        await db.SaveChangesAsync(ct);
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
