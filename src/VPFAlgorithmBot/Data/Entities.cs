namespace VPFAlgorithmBot.Data;

public sealed class AppUser
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Username { get; set; }
    public string Role { get; set; } = "operator";
    public string Status { get; set; } = "pending";
    public List<UserScope> Scopes { get; set; } = [];
}
public sealed class UserScope
{
    public int UserId { get; set; }
    public int ObjectId { get; set; }
}
public sealed class PendingCustomAnswer
{
    public int UserId { get; set; }
    public long IncidentId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public long? ChatTelegramId { get; set; }
    public int? PromptMessageTelegramId { get; set; }
}
public sealed class SourceChat
{
    public int Id { get; set; }
    public long TelegramChatId { get; set; }
    public long SenderTelegramId { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class MonitoredObject
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class ProblemCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class AlgorithmRule
{
    public int Id { get; set; }
    public int ObjectId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string MatchPattern { get; set; } = "";
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}
public sealed class RouteRule
{
    public int Id { get; set; }
    public int? ChatId { get; set; }
    public int? ObjectId { get; set; }
    public int? CategoryId { get; set; }
    public int UserId { get; set; }
    public int Priority { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}
public sealed class ResponseTemplate
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public int? AlgorithmRuleId { get; set; }
    public int? ObjectId { get; set; }
    public int? CategoryId { get; set; }
    public int SortOrder { get; set; } = 100;
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}
public sealed class TemplateVersion
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset SavedAtUtc { get; set; }
}
public sealed class IncomingMessage
{
    public long Id { get; set; }
    public int ChatId { get; set; }
    public int TelegramMessageId { get; set; }
    public long SenderTelegramId { get; set; }
    public DateTimeOffset TelegramDateUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string Text { get; set; } = "";
    public string Status { get; set; } = "new";
    public string? ParseError { get; set; }
    public long? IncidentId { get; set; }
}
public sealed class Incident
{
    public long Id { get; set; }
    public int ChatId { get; set; }
    public int ObjectId { get; set; }
    public int CategoryId { get; set; }
    public int AlgorithmRuleId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public long StartMessageId { get; set; }
    public long? EndMessageId { get; set; }
    public string Quality { get; set; } = "complete";
    public List<IncidentResponse> Responses { get; set; } = [];
    public string ProblemState => EndedAtUtc is null ? "Active" : "Resolved";
    public string AnswerState => Responses.Count == 0 ? "Unanswered" : "Answered";
}
public sealed class IncidentResponse
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public int UserId { get; set; }
    public int? TemplateId { get; set; }
    public int? TemplateVersion { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? ActionKey { get; set; }
}
public sealed class NotificationOutbox
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = "problem";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}
public sealed class ChatInstructionOutbox
{
    public long Id { get; set; }
    public int ChatId { get; set; }
    public string Kind { get; set; } = "initial";
    public int EventMessageId { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}
public sealed class AuditEvent
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public long? EntityId { get; set; }
    public string Detail { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
