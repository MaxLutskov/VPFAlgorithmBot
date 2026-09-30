namespace VPFAlgorithmBot.Telegram;

public sealed class WebhookRegistrationWorker(TelegramClient telegram, IConfiguration config,
    ILogger<WebhookRegistrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (config["Telegram:Mode"] != "Webhook") return;
        var host = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
        var secret = config["Telegram:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning("Telegram webhook registration skipped: Azure hostname or webhook secret is missing");
            return;
        }
        var url = $"https://{host}/api/telegram/{Uri.EscapeDataString(secret)}";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await telegram.SetWebhookAsync(url, stoppingToken);
                logger.LogInformation("Telegram webhook registered for Azure Web App");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Telegram webhook registration failed ({ErrorType}); retrying", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
