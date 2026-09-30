using System.Globalization;
using System.Text;
using System.Text.Json;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Data;

public sealed record TelegramDesktopImportResult(int EligibleMessages, int ImportedMessages, int Duplicates, int NeedsReview);

public static class TelegramDesktopImport
{
    public static async Task<TelegramDesktopImportResult> ImportAsync(Stream json, SourceChat chat,
        IncidentService incidents, DateTimeOffset now, CancellationToken ct = default)
    {
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            throw new FormatException("Експортуйте один чат у Telegram Desktop у форматі JSON (result.json).");
        var cutoff = now.AddDays(-3);
        var eligible = new List<InboundEvent>();
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object) continue;
            if (!message.TryGetProperty("type", out var type) || type.GetString() != "message" ||
                !message.TryGetProperty("id", out var id) || !id.TryGetInt32(out var messageId) ||
                !message.TryGetProperty("date_unixtime", out var unixTime) ||
                !long.TryParse(unixTime.ToString(), CultureInfo.InvariantCulture, out var seconds)) continue;
            var sent = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (sent < cutoff || sent > now.AddMinutes(5)) continue;
            if (!message.TryGetProperty("text", out var textField)) continue;
            var text = ReadText(textField);
            if (EventParser.Kind(text) is null) continue;
            var senderId = SenderId(message) ?? chat.SenderTelegramId;
            if (chat.SenderTelegramId != 0 && senderId != chat.SenderTelegramId) continue;
            eligible.Add(new InboundEvent(chat.TelegramChatId, senderId, messageId, sent, text));
        }
        var imported = 0;
        var duplicates = 0;
        var review = 0;
        foreach (var item in eligible.OrderBy(x => x.TelegramDateUtc).ThenBy(x => x.MessageTelegramId))
        {
            var result = await incidents.IngestFromExportAsync(item, ct);
            if (result.Status is "Created" or "Resolved") imported++;
            else if (result.Status == "Duplicate") duplicates++;
            else if (result.Status == "Review") review++;
        }
        return new(eligible.Count, imported, duplicates, review);
    }

    private static long? SenderId(JsonElement message)
    {
        if (!message.TryGetProperty("from_id", out var field) || field.ValueKind != JsonValueKind.String) return null;
        var value = field.GetString() ?? "";
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return long.TryParse(digits, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static string ReadText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind != JsonValueKind.Array) return "";
        var text = new StringBuilder();
        foreach (var part in value.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String) text.Append(part.GetString());
            else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var inner) && inner.ValueKind == JsonValueKind.String)
                text.Append(inner.GetString());
        }
        return text.ToString();
    }
}
