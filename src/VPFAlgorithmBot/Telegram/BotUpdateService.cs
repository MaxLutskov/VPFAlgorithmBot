using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Telegram;

public sealed class BotUpdateService(AlgorithmDbContext db, IncidentService incidents, TelegramClient telegram,
    IConfiguration config, ILogger<BotUpdateService> logger)
{
    public async Task HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(callback, ct);
            return;
        }
        var message = update.Message ?? update.ChannelPost;
        if (message is null || string.IsNullOrWhiteSpace(message.Text)) return;
        var sender = message.From;
        if (message.Chat.Type != "private")
        {
            var senderId = sender?.Id ?? message.SenderChat?.Id ?? 0;
            var result = await incidents.IngestAsync(new InboundEvent(message.Chat.Id, senderId, message.MessageId,
                DateTimeOffset.FromUnixTimeSeconds(message.Date), message.Text), ct);
            logger.LogInformation("Telegram source update: chat={ChatId}, sender={SenderId}, message={MessageId}, status={Status}, reason={Reason}",
                message.Chat.Id, senderId, message.MessageId, result.Status, result.Reason);
            return;
        }
        if (sender is null) return;
        if (message.Text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var user = await db.Users.SingleOrDefaultAsync(x => x.TelegramId == sender.Id, ct);
            if (user is null)
            {
                var adminId = long.TryParse(config["Admin:BootstrapTelegramId"], out var parsed) ? parsed : 0;
                user = new AppUser
                {
                    TelegramId = sender.Id, Username = sender.Username, DisplayName = sender.DisplayName,
                    Role = sender.Id == adminId ? "admin" : "operator",
                    Status = sender.Id == adminId ? "approved" : "pending"
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }
            await SendBestEffortAsync(sender.Id, user.Status == "approved" ? "Доступ активний." : "Запит доступу надіслано адміністратору.", null, ct);
            return;
        }
        var approved = await db.Users.SingleOrDefaultAsync(x => x.TelegramId == sender.Id && x.Status == "approved", ct);
        if (approved is null) return;
        var actionKey = $"message:{message.Chat.Id}:{message.MessageId}";
        if (await db.Responses.AnyAsync(x => x.ActionKey == actionKey, ct)) return;
        if (message.Text.StartsWith('/'))
        {
            await SendBestEffortAsync(sender.Id, "Натисніть «Надати відповідь» під повідомленням потрібного алгоритму.", null, ct);
            return;
        }
        var pending = await db.PendingCustomAnswers.FindAsync([approved.Id], ct);
        if (pending is null)
        {
            await SendBestEffortAsync(sender.Id, "Спочатку оберіть алгоритм кнопкою «Надати відповідь», потім натисніть «Своя відповідь».", null, ct);
            return;
        }
        if (pending.RequestedAtUtc < DateTimeOffset.UtcNow.AddHours(-24))
        {
            db.PendingCustomAnswers.Remove(pending);
            await db.SaveChangesAsync(ct);
            await SendBestEffortAsync(sender.Id, "Час для цієї відповіді минув. Оберіть алгоритм знову.", null, ct);
            return;
        }
        try
        {
            await incidents.RespondAsync(pending.IncidentId, sender.Id, message.Text, null, actionKey, ct);
            db.PendingCustomAnswers.Remove(pending);
            await db.SaveChangesAsync(ct);
            await SendBestEffortAsync(sender.Id, "Відповідь збережено.", null, ct);
        }
        catch (ArgumentException ex)
        {
            await SendBestEffortAsync(sender.Id, $"{ex.Message} Напишіть відповідь ще раз.", null, ct);
        }
        catch (UnauthorizedAccessException)
        {
            db.PendingCustomAnswers.Remove(pending);
            await db.SaveChangesAsync(ct);
            await SendBestEffortAsync(sender.Id, "Немає доступу до цього алгоритму. Оберіть інший.", null, ct);
        }
    }

    private async Task HandleCallbackAsync(TelegramCallback callback, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.TelegramId == callback.From.Id && x.Status == "approved", ct);
        if (user is null)
        {
            await AnswerCallbackBestEffortAsync(callback.Id, "Доступ не підтверджено", ct);
            return;
        }
        var parts = callback.Data.Split(':');
        if (parts.Length < 2 || !long.TryParse(parts[1], out var incidentId)) return;
        var incident = await db.Incidents.FindAsync([incidentId], ct);
        if (incident is null) return;
        var allowed = user.Role == "admin" || (user.Role == "operator" && await db.UserScopes.AnyAsync(x => x.UserId == user.Id && x.ObjectId == incident.ObjectId, ct));
        if (!allowed)
        {
            await AnswerCallbackBestEffortAsync(callback.Id, "Немає доступу до об’єкта", ct);
            return;
        }
        if (parts[0] == "templates")
        {
            var templates = await db.ResponseTemplates.Where(x => x.Enabled && x.AlgorithmRuleId == incident.AlgorithmRuleId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Take(30).ToListAsync(ct);
            var buttons = templates.Select(x => new[] { new { text = x.Title, callback_data = $"template:{incidentId}:{x.Id}" } })
                .Append([new { text = "✍️ Своя відповідь", callback_data = $"custom:{incidentId}" }]).ToArray();
            var obj = await db.Objects.FindAsync([incident.ObjectId], ct);
            var rule = await db.AlgorithmRules.FindAsync([incident.AlgorithmRuleId], ct);
            await AnswerCallbackBestEffortAsync(callback.Id, "Оберіть відповідь", ct);
            await SendBestEffortAsync(callback.From.Id,
                $"{obj?.Name} — {rule?.Name}\nОберіть готову відповідь або «Своя відповідь»:", new { inline_keyboard = buttons }, ct);
        }
        else if (parts[0] == "custom")
        {
            var pending = await db.PendingCustomAnswers.FindAsync([user.Id], ct);
            if (pending is null) db.PendingCustomAnswers.Add(new PendingCustomAnswer { UserId = user.Id, IncidentId = incidentId, RequestedAtUtc = DateTimeOffset.UtcNow });
            else { pending.IncidentId = incidentId; pending.RequestedAtUtc = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(ct);
            var obj = await db.Objects.FindAsync([incident.ObjectId], ct);
            var rule = await db.AlgorithmRules.FindAsync([incident.AlgorithmRuleId], ct);
            await AnswerCallbackBestEffortAsync(callback.Id, "Напишіть відповідь", ct);
            await SendBestEffortAsync(callback.From.Id, $"{obj?.Name} — {rule?.Name}\nНапишіть свою відповідь звичайним повідомленням:",
                new { force_reply = true, input_field_placeholder = "Ваша відповідь" }, ct);
        }
        else if (parts[0] == "template" && parts.Length == 3 && int.TryParse(parts[2], out var templateId))
        {
            try { await incidents.RespondAsync(incidentId, callback.From.Id, "", templateId, $"callback:{callback.Id}", ct); }
            catch (ArgumentException)
            {
                await AnswerCallbackBestEffortAsync(callback.Id, "Ця відповідь уже недоступна. Оберіть іншу.", ct);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                await AnswerCallbackBestEffortAsync(callback.Id, "Немає доступу до цієї відповіді", ct);
                return;
            }
            var pending = await db.PendingCustomAnswers.FindAsync([user.Id], ct);
            if (pending is not null) { db.PendingCustomAnswers.Remove(pending); await db.SaveChangesAsync(ct); }
            await AnswerCallbackBestEffortAsync(callback.Id, "Відповідь збережено", ct);
            await SendBestEffortAsync(callback.From.Id, "Відповідь збережено.", null, ct);
        }
    }

    private async Task SendBestEffortAsync(long chatId, string text, object? markup, CancellationToken ct)
    {
        try { await telegram.SendAsync(chatId, text, markup, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Telegram sendMessage failed: {ErrorType}, HTTP {StatusCode}",
                ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
        }
    }

    private async Task AnswerCallbackBestEffortAsync(string callbackId, string text, CancellationToken ct)
    {
        try { await telegram.AnswerCallbackAsync(callbackId, text, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Telegram answerCallbackQuery failed: {ErrorType}, HTTP {StatusCode}",
                ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
        }
    }
}
