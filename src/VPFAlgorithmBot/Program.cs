using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using VPFAlgorithmBot.Api;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;
using VPFAlgorithmBot.Telegram;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var mode = builder.Configuration["Telegram:Mode"] ?? "Demo";
if (mode is not ("Demo" or "Polling" or "Webhook")) throw new InvalidOperationException("Unknown Telegram:Mode");
if (mode == "Demo" && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("Demo mode is available only in Development");
var connection = builder.Configuration.GetConnectionString("BotDatabase");
if (mode != "Demo" && string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:BotDatabase is required");
builder.Services.AddDbContext<AlgorithmDbContext>(options =>
{
    if (mode == "Demo") options.UseInMemoryDatabase("vpf-demo");
    else options.UseSqlServer(connection, sql =>
    {
        sql.MigrationsHistoryTable("__EFMigrationsHistory", AlgorithmDbContext.Schema);
        sql.EnableRetryOnFailure(3);
    });
});
var keyPath = builder.Configuration["Admin:KeyPath"];
if (string.IsNullOrWhiteSpace(keyPath))
{
    var azureHome = Environment.GetEnvironmentVariable("HOME");
    keyPath = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")) &&
              !string.IsNullOrWhiteSpace(azureHome)
        ? Path.Combine(azureHome, "data-protection-keys")
        : Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys");
}
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<BotUpdateService>();
builder.Services.AddScoped<MiniAppAuth>();
builder.Services.AddScoped<AdminSession>();
builder.Services.AddHttpClient<TelegramClient>(client => client.Timeout = TimeSpan.FromSeconds(40));
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<TelegramPollingWorker>();
builder.Services.AddHostedService<WebhookRegistrationWorker>();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "VPFAlgorithmBot", miniApp = "/miniapp/" }));
app.MapGet("/miniapp/", (HttpRequest request) => Results.Redirect("/miniapp/index.html" + request.QueryString));
app.MapPost("/api/telegram/{secret}", async (string secret, TelegramUpdate update, BotUpdateService bot, IConfiguration cfg, CancellationToken ct) =>
{
    var expected = cfg["Telegram:WebhookSecret"];
    if (cfg["Telegram:Mode"] != "Webhook" || string.IsNullOrWhiteSpace(expected) ||
        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(expected))) return Results.NotFound();
    await bot.HandleAsync(update, ct);
    return Results.Ok();
});
app.MapMiniAppApi();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AlgorithmDbContext>();
    if (mode == "Demo")
    {
        await db.Database.EnsureCreatedAsync();
        if (builder.Configuration.GetValue<bool>("Demo:SeedSampleData")) await DemoSeeder.SeedAsync(db);
    }
    else
    {
        await db.Database.MigrateAsync();
    }
    await SharedAlgorithmUpgrade.ApplyAsync(db);
    await LegacyArchiveCleanup.ApplyAsync(db);
    if (mode != "Demo")
    {
        var report = await WaterWorkbookSeed.ApplyAsync(db);
        app.Logger.LogInformation("Water workbook catalog reconciliation: {Report}", report);
    }
}
app.Run();
public partial class Program;
