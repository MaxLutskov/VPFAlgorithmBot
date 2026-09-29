using System.Text.Json.Serialization;

namespace VPFAlgorithmBot.Telegram;

public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    [JsonPropertyName("callback_query")] public TelegramCallback? CallbackQuery { get; set; }
}
public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")] public int MessageId { get; set; }
    [JsonPropertyName("date")] public long Date { get; set; }
    [JsonPropertyName("chat")] public TelegramChat Chat { get; set; } = new();
    [JsonPropertyName("from")] public TelegramUser? From { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}
public sealed class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}
public sealed class TelegramUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = "";
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    public string DisplayName => string.Join(' ', new[] { FirstName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
public sealed class TelegramCallback
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("from")] public TelegramUser From { get; set; } = new();
    [JsonPropertyName("data")] public string Data { get; set; } = "";
}
