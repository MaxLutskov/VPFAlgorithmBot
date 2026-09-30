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
        if (message.Text.StartsWith("/answer ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = message.Text.Split(' ', 3);
            if (parts.Length < 3 || !long.TryParse(parts[1], out var id)) return;
            await incidents.RespondAsync(id, sender.Id, parts[2], null, $"message:{message.Chat.Id}:{message.MessageId}", ct);
            await SendBestEffortAsync(sender.Id, $"Відповідь на алгоритм №{id} збережено.", null, ct);
            return;
        }
        await SendBestEffortAsync(sender.Id, "Щоб надати власну відповідь, надішліть: /answer НОМЕР текст. Або оберіть готову відповідь під повідомленням алгоритму.", null, ct);
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
            var buttons = templates.Select(x => new[] { new { text = x.Title, callback_data = $"template:{incidentId}:{x.Id}" } }).ToArray();
            await AnswerCallbackBestEffortAsync(callback.Id, "Оберіть відповідь", ct);
            await SendBestEffortAsync(callback.From.Id,
                $"Алгоритм №{incidentId}: оберіть готову відповідь або напишіть /answer {incidentId} текст", new { inline_keyboard = buttons }, ct);
        }
        else if (parts[0] == "template" && parts.Length == 3 && int.TryParse(parts[2], out var templateId))
        {
            await incidents.RespondAsync(incidentId, callback.From.Id, "", templateId, $"callback:{callback.Id}", ct);
            await AnswerCallbackBestEffortAsync(callback.Id, "Відповідь збережено", ct);
            await SendBestEffortAsync(callback.From.Id, $"Відповідь на алгоритм №{incidentId} збережено.", null, ct);
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
