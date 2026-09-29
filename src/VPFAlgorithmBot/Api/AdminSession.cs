using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace VPFAlgorithmBot.Api;

public sealed class AdminSession(IDataProtectionProvider provider, IConfiguration config, IWebHostEnvironment env)
{
    private readonly IDataProtector protector = provider.CreateProtector("VPFAlgorithmBot.Admin.v1");
    private string Password => !string.IsNullOrWhiteSpace(config["Admin:Password"])
        ? config["Admin:Password"]! : (env.IsDevelopment() && config["Telegram:Mode"] == "Demo" ? "demo-admin" : "");

    public string? SignIn(string password, long telegramId)
    {
        if (Password.Length == 0) return null;
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? ""));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(Password));
        if (!CryptographicOperations.FixedTimeEquals(left, right)) return null;
        return protector.Protect(JsonSerializer.Serialize(new Payload(telegramId, DateTimeOffset.UtcNow.AddHours(8))));
    }
    public bool Valid(HttpRequest request, long telegramId)
    {
        var token = request.Headers["X-Admin-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(token));
            return payload is not null && payload.TelegramId == telegramId && payload.ExpiresAtUtc > DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return false; }
    }
    private sealed record Payload(long TelegramId, DateTimeOffset ExpiresAtUtc);
}
