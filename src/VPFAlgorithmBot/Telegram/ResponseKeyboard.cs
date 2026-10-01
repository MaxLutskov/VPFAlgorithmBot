using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Telegram;

public static class ResponseKeyboard
{
    public static object Build(long incidentId, IEnumerable<ResponseTemplate> templates) => new
    {
        inline_keyboard = templates.Select(x => new[] { new { text = x.Title, callback_data = $"template:{incidentId}:{x.Id}" } })
            .Append([new { text = "✍️ Своя відповідь", callback_data = $"custom:{incidentId}" }]).ToArray()
    };
}
