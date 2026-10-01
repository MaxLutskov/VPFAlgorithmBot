using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Domain;

public sealed record InboundEvent(long ChatTelegramId, long SenderTelegramId, int MessageTelegramId, DateTimeOffset TelegramDateUtc, string Text);
public sealed record IngestResult(string Status, long? IncidentId = null, string? Reason = null);

public sealed class IncidentService(AlgorithmDbContext db)
{
    public Task<IngestResult> IngestAsync(InboundEvent input, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, () => IngestCoreAsync(input, false, ct), ct);

    public Task<IngestResult> IngestFromExportAsync(InboundEvent input, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, () => IngestCoreAsync(input, true, ct), ct);

    private async Task<IngestResult> IngestCoreAsync(InboundEvent input, bool fromExport, CancellationToken ct)
    {
        var chat = await db.Chats.SingleOrDefaultAsync(x => x.TelegramChatId == input.ChatTelegramId && x.Enabled, ct);
        if (chat is null) return new("Ignored", Reason: "Невідомий чат");
        if (chat.SenderTelegramId != 0 && chat.SenderTelegramId != input.SenderTelegramId)
            return new("Ignored", Reason: "Невідомий відправник");
        var previous = await db.IncomingMessages.AsNoTracking().SingleOrDefaultAsync(x => x.ChatId == chat.Id && x.TelegramMessageId == input.MessageTelegramId, ct);
        if (previous is not null) return new("Duplicate", previous.IncidentId);

        var message = new IncomingMessage
        {
            ChatId = chat.Id, TelegramMessageId = input.MessageTelegramId,
            SenderTelegramId = input.SenderTelegramId, TelegramDateUtc = input.TelegramDateUtc,
            ReceivedAtUtc = DateTimeOffset.UtcNow, Text = input.Text.Length > 4000 ? input.Text[..4000] : input.Text
        };
        var kind = EventParser.Kind(input.Text);
        if (kind is null)
        {
            message.Status = "ignored";
            db.IncomingMessages.Add(message);
            await db.SaveChangesAsync(ct);
            return new("Ignored", Reason: "Немає маркера");
        }
        AlgorithmRule rule;
        MonitoredObject obj;
        DateTimeOffset occurred;
        try
        {
            occurred = EventParser.ParseTime(input.TelegramDateUtc, input.Text);
            rule = await AlgorithmCatalog.ResolveAsync(db, input.Text, ct);
            var description = EventParser.Describe(input.Text);
            obj = await db.Objects.SingleAsync(x => x.Code == description.ObjectCode, ct);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.RegularExpressions.RegexMatchTimeoutException or ArgumentException)
        {
            message.Status = "review";
            message.ParseError = ex.Message;
            db.IncomingMessages.Add(message);
            await db.SaveChangesAsync(ct);
            return new("Review", Reason: ex.Message);
        }

        db.IncomingMessages.Add(message);
        await db.SaveChangesAsync(ct);
        if (kind == "problem")
        {
            var sameStart = await db.Incidents.AsNoTracking().Where(x => x.ChatId == chat.Id &&
                x.ObjectId == obj.Id && x.AlgorithmRuleId == rule.Id && x.StartedAtUtc == occurred)
                .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (sameStart is not null)
            {
                message.Status = "duplicate";
                message.IncidentId = sameStart.Id;
                await db.SaveChangesAsync(ct);
                return new("Duplicate", sameStart.Id);
            }
            if (await db.Incidents.AnyAsync(x => x.ChatId == chat.Id && x.ObjectId == obj.Id && x.AlgorithmRuleId == rule.Id && x.EndedAtUtc == null, ct))
            {
                message.Status = "review";
                message.ParseError = "Повторне червоне повідомлення для відкритого алгоритму";
                await db.SaveChangesAsync(ct);
                return new("Review", Reason: message.ParseError);
            }
            var incident = new Incident
            {
                ChatId = chat.Id, ObjectId = obj.Id, CategoryId = rule.CategoryId,
                AlgorithmRuleId = rule.Id, StartedAtUtc = occurred, StartMessageId = message.Id,
                Quality = fromExport ? "export_missing_end" : "complete"
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);
            message.Status = fromExport ? "imported" : "processed";
            message.IncidentId = incident.Id;
            if (!fromExport) await QueueNotificationsAsync(incident, "problem", ct);
            await db.SaveChangesAsync(ct);
            return new("Created", incident.Id);
        }
        var candidates = await db.Incidents.Where(x => x.ChatId == chat.Id && x.ObjectId == obj.Id && x.AlgorithmRuleId == rule.Id && x.EndedAtUtc == null).ToListAsync(ct);
        if (candidates.Count == 0)
        {
            var sameEnd = await db.Incidents.AsNoTracking().Where(x => x.ChatId == chat.Id &&
                x.ObjectId == obj.Id && x.AlgorithmRuleId == rule.Id && x.EndedAtUtc == occurred)
                .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (sameEnd is not null)
            {
                message.Status = "duplicate";
                message.IncidentId = sameEnd.Id;
                await db.SaveChangesAsync(ct);
                return new("Duplicate", sameEnd.Id);
            }
        }
        if (candidates.Count == 0 && fromExport && EventParser.Duration(input.Text) is { } duration)
        {
            var inferred = new Incident
            {
                ChatId = chat.Id, ObjectId = obj.Id, CategoryId = rule.CategoryId,
                AlgorithmRuleId = rule.Id, StartedAtUtc = occurred - duration,
                EndedAtUtc = occurred, StartMessageId = message.Id, EndMessageId = message.Id,
                Quality = "export_inferred_start"
            };
            db.Incidents.Add(inferred);
            await db.SaveChangesAsync(ct);
            message.Status = "imported";
            message.IncidentId = inferred.Id;
            await db.SaveChangesAsync(ct);
            return new("Resolved", inferred.Id);
        }
        if (candidates.Count != 1 || occurred < candidates[0].StartedAtUtc)
        {
            message.Status = "review";
            message.ParseError = candidates.Count == 0 ? "Немає відповідного початку" : candidates.Count > 1 ? "Кілька відкритих випадків" : "Завершення раніше початку";
            await db.SaveChangesAsync(ct);
            return new("Review", Reason: message.ParseError);
        }
        var current = candidates[0];
        current.EndedAtUtc = occurred;
        current.EndMessageId = message.Id;
        if (fromExport && current.Quality == "export_missing_end") current.Quality = "export_complete";
        message.Status = fromExport ? "imported" : "processed";
        message.IncidentId = current.Id;
        if (!fromExport) await QueueNotificationsAsync(current, "recovery", ct);
        await db.SaveChangesAsync(ct);
        return new("Resolved", current.Id);
    }

    private async Task QueueNotificationsAsync(Incident incident, string kind, CancellationToken ct)
    {
        if (kind == "problem")
            db.NotificationOutbox.Add(new NotificationOutbox
            {
                IncidentId = incident.Id, UserId = 0, Kind = "chat_prompt",
                Text = "Варіанти відповіді для чату алгоритму",
                DueAtUtc = DateTimeOffset.UtcNow
            });
        // Assignments depend on the object, not on legacy chat/category fields.
        var recipients = await db.RouteRules.AsNoTracking()
            .Where(x => x.Enabled && x.ObjectId == incident.ObjectId)
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        if (recipients.Length == 0) return;
        var valid = await db.Users.Where(x => recipients.Contains(x.Id) && x.Status == "approved").Select(x => x.Id).ToListAsync(ct);
        var rule = await db.AlgorithmRules.FindAsync([incident.AlgorithmRuleId], ct);
        var obj = await db.Objects.FindAsync([incident.ObjectId], ct);
        foreach (var userId in valid)
        {
            db.NotificationOutbox.Add(new NotificationOutbox
            {
                IncidentId = incident.Id, UserId = userId, Kind = kind,
                Text = kind == "problem" ? $"🔴 {obj?.Name} — {rule?.Name}. Надати відповідь можна також після завершення."
                    : $"🟢 {obj?.Name} — {rule?.Name}: завершено. Якщо відповіді ще немає, її можна надати зараз.",
                DueAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public Task<IncidentResponse> RespondAsync(long incidentId, long telegramUserId, string text, int? templateId, string actionKey,
        CancellationToken ct = default, bool confirmInSourceChat = false) =>
        DatabaseWork.RunAsync(db, () => RespondCoreAsync(incidentId, telegramUserId, text, templateId, actionKey, confirmInSourceChat, ct), ct);

    private async Task<IncidentResponse> RespondCoreAsync(long incidentId, long telegramUserId, string text, int? templateId,
        string actionKey, bool confirmInSourceChat, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) && templateId is null) throw new ArgumentException("Порожня відповідь");
        var old = await db.Responses.SingleOrDefaultAsync(x => x.ActionKey == actionKey, ct);
        if (old is not null) return old;
        var user = await db.Users.Include(x => x.Scopes).SingleOrDefaultAsync(x => x.TelegramId == telegramUserId && x.Status == "approved", ct)
            ?? throw new UnauthorizedAccessException("Доступ не підтверджено");
        var incident = await db.Incidents.FindAsync([incidentId], ct) ?? throw new ArgumentException("Алгоритм не знайдено");
        if (user.Role != "admin" && (user.Role != "operator" || !user.Scopes.Any(x => x.ObjectId == incident.ObjectId)))
            throw new UnauthorizedAccessException("Немає доступу до об’єкта");
        ResponseTemplate? template = null;
        if (templateId is not null)
        {
            template = await db.ResponseTemplates.SingleOrDefaultAsync(x => x.Id == templateId && x.Enabled, ct)
                ?? throw new ArgumentException("Шаблон не знайдено");
            if (template.AlgorithmRuleId != incident.AlgorithmRuleId)
                throw new UnauthorizedAccessException("Шаблон недоступний для алгоритму");
            text = template.Text + (string.IsNullOrWhiteSpace(text) ? "" : "\n" + text.Trim());
        }
        if (text.Length > 2000) throw new ArgumentException("Відповідь завелика");
        var response = new IncidentResponse
        {
            IncidentId = incidentId, UserId = user.Id, TemplateId = template?.Id,
            TemplateVersion = template?.Version, Text = text.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow, ActionKey = actionKey
        };
        db.Responses.Add(response);
        db.AuditEvents.Add(new AuditEvent { ActorUserId = user.Id, Action = "respond", Entity = "incident", EntityId = incidentId, CreatedAtUtc = response.CreatedAtUtc });
        await db.SaveChangesAsync(ct);
        if (confirmInSourceChat)
        {
            db.NotificationOutbox.Add(new NotificationOutbox
            {
                IncidentId = incidentId, UserId = 0, Kind = $"response_receipt:{response.Id}",
                Text = $"✅ Відповідь від {user.DisplayName}:\n{response.Text}",
                DueAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        return response;
    }
}
