using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;

namespace VPFAlgorithmBot.Api;

public sealed class MiniAppAuth(IConfiguration config, IWebHostEnvironment env)
{
    public async Task<AppUser?> GetUserAsync(HttpRequest request, AlgorithmDbContext db, CancellationToken ct)
    {
        var demo = config["Telegram:Mode"] == "Demo" && env.IsDevelopment();
        if (demo && int.TryParse(request.Headers["X-Demo-User-Id"], out var demoId))
        {
            var demoUser = await db.Users.FindAsync([demoId], ct);
            return demoUser?.Status == "approved" ? demoUser : null;
        }
        var initData = request.Headers["X-Telegram-Init-Data"].FirstOrDefault();
        var token = config["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(initData) || string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var values = QueryHelpers.ParseQuery(initData);
            if (!values.TryGetValue("hash", out var hash)) return null;
            var check = string.Join('\n', values.Where(x => x.Key != "hash")
                .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
            var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
            var actual = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check));
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(hash.ToString()))) return null;
            if (!values.TryGetValue("auth_date", out var dateText) || !long.TryParse(dateText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return null;
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (age > TimeSpan.FromHours(24) || age < TimeSpan.FromMinutes(-5)) return null;
            if (!values.TryGetValue("user", out var userJson)) return null;
            var user = JsonSerializer.Deserialize<InitUser>(userJson.ToString());
            if (user is null) return null;
            return await db.Users.SingleOrDefaultAsync(x => x.TelegramId == user.Id && x.Status == "approved", ct);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return null; }
    }

    private sealed class InitUser
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }
}
