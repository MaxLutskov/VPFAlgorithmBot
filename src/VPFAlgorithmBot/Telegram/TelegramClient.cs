using System.Net.Http.Json;
using System.Text.Json;

namespace VPFAlgorithmBot.Telegram;

public sealed class TelegramClient(HttpClient http, IConfiguration config)
{
    private string Token => config["Telegram:BotToken"] ?? "";
    public bool Configured => !string.IsNullOrWhiteSpace(Token);

    private async Task<JsonElement> CallAsync(string method, object payload, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("Telegram BotToken не налаштовано");
        using var response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{Token}/{method}", payload, ct);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Telegram повернув порожню відповідь");
        if (!body.RootElement.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException("Помилка Telegram API");
        return body.RootElement.GetProperty("result").Clone();
    }

    public Task<JsonElement> SendAsync(long chatId, string text, object? markup, CancellationToken ct) =>
        markup is null
            ? CallAsync("sendMessage", new { chat_id = chatId, text }, ct)
            : CallAsync("sendMessage", new { chat_id = chatId, text, reply_markup = markup }, ct);
    public Task<JsonElement> SendReplyAsync(long chatId, int messageId, string text, CancellationToken ct) =>
        CallAsync("sendMessage", new { chat_id = chatId, text,
            reply_parameters = new { message_id = messageId, allow_sending_without_reply = true } }, ct);
    public Task<JsonElement> AnswerCallbackAsync(string id, string text, CancellationToken ct) =>
        CallAsync("answerCallbackQuery", new { callback_query_id = id, text }, ct);
    public Task<JsonElement> DeleteWebhookAsync(CancellationToken ct) =>
        CallAsync("deleteWebhook", new { drop_pending_updates = false }, ct);
    public Task<JsonElement> GetWebhookInfoAsync(CancellationToken ct) =>
        CallAsync("getWebhookInfo", new { }, ct);
    public Task<JsonElement> SetWebhookAsync(string url, CancellationToken ct) =>
        CallAsync("setWebhook", new { url, allowed_updates = new[] { "message", "channel_post", "callback_query" },
            drop_pending_updates = false }, ct);
    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        var json = await CallAsync("getUpdates", new { offset, timeout = 25, allowed_updates = new[] { "message", "channel_post", "callback_query" } }, ct);
        return JsonSerializer.Deserialize<List<TelegramUpdate>>(json.GetRawText()) ?? [];
    }
}
