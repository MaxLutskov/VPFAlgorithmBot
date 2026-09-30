using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Data;
using VPFAlgorithmBot.Domain;
using VPFAlgorithmBot.Telegram;

namespace VPFAlgorithmBot.Api;

public static class MiniAppEndpoints
{
    public static void MapMiniAppApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/miniapp").AddEndpointFilter<AdminValidationFilter>();
        api.MapGet("/me", async (HttpRequest req, MiniAppAuth auth, AlgorithmDbContext db, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(req, db, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(new { user.Id, user.DisplayName, user.Role, user.Status });
        });
        api.MapGet("/dashboard", async (HttpRequest req, MiniAppAuth auth, AlgorithmDbContext db, string? period, string? fromDate, string? toDate, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(req, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!IncidentPeriod.TryApply(Visible(db, user).AsNoTracking(), period, fromDate, toDate,
                DateTimeOffset.UtcNow, out var query, out var error)) return Results.BadRequest(error);
            return Results.Ok(new
            {
                Total = await query.CountAsync(ct),
                Active = await query.CountAsync(x => x.EndedAtUtc == null, ct),
                Unanswered = await query.CountAsync(x => !x.Responses.Any(), ct),
                ResolvedUnanswered = await query.CountAsync(x => x.EndedAtUtc != null && !x.Responses.Any(), ct)
            });
        });
        api.MapGet("/lookups", async (HttpRequest req, MiniAppAuth auth, AlgorithmDbContext db, CancellationToken ct) =>
        {
            var user=await auth.GetUserAsync(req,db,ct);if(user is null)return Results.Unauthorized();
            var ids=user.Role=="admin" ? await db.Objects.Select(x=>x.Id).ToListAsync(ct)
                : await db.UserScopes.Where(x=>x.UserId==user.Id).Select(x=>x.ObjectId).ToListAsync(ct);
            var objects = await db.Objects.AsNoTracking().ToListAsync(ct);
            var families = objects.Where(x => ids.Contains(x.Id)).Select(x => AlgorithmCatalog.Family(x.Code)).ToHashSet();
            var rules = await db.AlgorithmRules.AsNoTracking().ToListAsync(ct);
            var canonical = AlgorithmCatalog.CanonicalIds(rules, objects);
            return Results.Ok(new {
                Objects=objects.Where(x=>ids.Contains(x.Id)).Select(x=>new{x.Id,x.Name,x.Code}).ToList(),
                Chats=await db.Chats.AsNoTracking().Where(x=>x.Enabled).OrderBy(x=>x.Name)
                    .Select(x=>new{x.Id,x.Name}).ToListAsync(ct),
                Algorithms=rules.Where(x=>canonical[x.Id]==x.Id && families.Contains(AlgorithmCatalog.Family(objects.First(o=>o.Id==x.ObjectId).Code)))
                    .Select(x=>new{x.Id,x.Name}).ToList()
            });
        });
        api.MapGet("/incidents", async (HttpRequest req, MiniAppAuth auth, AlgorithmDbContext db, string? state, string? answer,
            string? period, string? fromDate, string? toDate, int? chatId, int? skip, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(req, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!IncidentPeriod.TryApply(Visible(db, user).AsNoTracking(), period, fromDate, toDate,
                DateTimeOffset.UtcNow, out var query, out var error)) return Results.BadRequest(error);
            if (state == "active") query = query.Where(x => x.EndedAtUtc == null);
            if (state == "resolved") query = query.Where(x => x.EndedAtUtc != null);
            if (chatId is not null) query = query.Where(x => x.ChatId == chatId);
            if (answer == "unanswered") query = query.Where(x => !x.Responses.Any());
            if (answer == "answered") query = query.Where(x => x.Responses.Any());
            var rows = await query.OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.Id)
                .Skip(Math.Max(0, skip ?? 0)).Take(100)
                .Select(x => new
                {
                    x.Id, x.ChatId, x.ObjectId, x.CategoryId, x.AlgorithmRuleId,
                    x.StartedAtUtc, x.EndedAtUtc, x.Quality,
                    ProblemState = x.EndedAtUtc == null ? "Active" : "Resolved",
                    AnswerState = x.Responses.Any() ? "Answered" : "Unanswered",
                    FirstResponseAtUtc = x.Responses.Min(r => (DateTimeOffset?)r.CreatedAtUtc),
                    ResponseCount = x.Responses.Count
                }).ToListAsync(ct);
            return Results.Ok(rows);
        });
        api.MapGet("/incidents/{id:long}", async (long id, HttpRequest req, MiniAppAuth auth, AlgorithmDbContext db, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(req, db, ct);
            if (user is null) return Results.Unauthorized();
            var incident = await Visible(db, user).Include(x => x.Responses).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (incident is null) return Results.NotFound();
            var names = await db.Users.Where(x => incident.Responses.Select(r => r.UserId).Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
            var templates = await db.ResponseTemplates.AsNoTracking()
                .Where(x => x.Enabled && x.AlgorithmRuleId == incident.AlgorithmRuleId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Title, x.Text }).ToListAsync(ct);
            return Results.Ok(new
            {
                incident.Id, incident.ChatId, incident.ObjectId, incident.CategoryId, incident.AlgorithmRuleId,
                incident.StartedAtUtc, incident.EndedAtUtc, incident.Quality,
                incident.ProblemState, incident.AnswerState,
                Templates = templates,
                Responses = incident.Responses.OrderBy(x => x.CreatedAtUtc).Select(x => new
                { x.Id, Author = names.GetValueOrDefault(x.UserId, ""), x.Text, x.CreatedAtUtc, x.TemplateId, x.TemplateVersion })
            });
        });
        api.MapPost("/incidents/{id:long}/responses", async (long id, ResponseEdit data, HttpRequest req,
            MiniAppAuth auth, AlgorithmDbContext db, IncidentService incidents, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(req, db, ct);
            if (user is null) return Results.Unauthorized();
            if (!await Visible(db, user).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            if (data.TemplateId is null && string.IsNullOrWhiteSpace(data.Text))
                return Results.BadRequest("Оберіть готову відповідь або введіть власний текст.");
            if (!Guid.TryParse(data.RequestId, out var requestId)) return Results.BadRequest("Некоректний ідентифікатор запиту.");
            try
            {
                var saved = await incidents.RespondAsync(id, user.TelegramId, data.Text ?? "", data.TemplateId,
                    $"miniapp:{requestId:N}", ct);
                return Results.Ok(new { saved.Id });
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
            catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
        });
        api.MapPost("/admin/signin", async (SignIn request, HttpRequest http, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            var user = await auth.GetUserAsync(http, db, ct);
            if (user?.Role != "admin") return Results.StatusCode(403);
            var token = sessions.SignIn(request.Password, user.TelegramId);
            return token is null ? Results.Unauthorized() : Results.Ok(new { token });
        });
        api.MapGet("/admin/catalog", async (HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var objects = await db.Objects.AsNoTracking().ToListAsync(ct);
            var rules = await db.AlgorithmRules.AsNoTracking().ToListAsync(ct);
            var canonical = AlgorithmCatalog.CanonicalIds(rules, objects);
            var canonicalIds = canonical.Values.Distinct().ToArray();
            return Results.Ok(new
            {
                Chats = await db.Chats.OrderBy(x => x.Id).ToListAsync(ct),
                Objects = objects.OrderBy(x => x.Code).ToList(),
                Categories = await db.Categories.OrderBy(x => x.Name).ToListAsync(ct),
                Algorithms = rules.Where(x => canonical[x.Id] == x.Id).OrderBy(x => AlgorithmCatalog.Family(objects.First(o => o.Id == x.ObjectId).Code))
                    .ThenBy(x => x.Name).Select(x => new { x.Id, x.ObjectId, x.CategoryId, x.Name, x.MatchPattern, x.Priority, x.Enabled,
                        Family = AlgorithmCatalog.Family(objects.First(o => o.Id == x.ObjectId).Code) }).ToList(),
                Routes = await db.RouteRules.OrderBy(x => x.Priority).ToListAsync(ct),
                Templates = await db.ResponseTemplates.Where(x => x.AlgorithmRuleId != null && canonicalIds.Contains(x.AlgorithmRuleId.Value))
                    .OrderBy(x => x.SortOrder).ToListAsync(ct),
                TemplateVersions = await db.TemplateVersions.OrderByDescending(x => x.SavedAtUtc).Take(100).ToListAsync(ct),
                Users = await db.Users.OrderBy(x => x.Id).ToListAsync(ct),
                Scopes = await db.UserScopes.ToListAsync(ct),
                Settings = await db.Settings.ToListAsync(ct),
                Reviews = await db.IncomingMessages.Where(x => x.Status == "review").OrderByDescending(x => x.Id).Take(100).ToListAsync(ct),
                Outbox = await db.NotificationOutbox.OrderByDescending(x => x.Id).Take(100).ToListAsync(ct)
                ,Audit = await db.AuditEvents.OrderByDescending(x => x.Id).Take(100).ToListAsync(ct)
            });
        });
        api.MapGet("/admin/source-status", async (HttpRequest req, MiniAppAuth auth, AdminSession sessions,
            AlgorithmDbContext db, TelegramClient telegram, IConfiguration config, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var webhook = await telegram.GetWebhookInfoAsync(ct);
            var url = webhook.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : null;
            var expectedPath = "/api/telegram/" + config["Telegram:WebhookSecret"];
            var webhookReady = Uri.TryCreate(url, UriKind.Absolute, out var webhookUri) &&
                webhookUri.AbsolutePath == expectedPath;
            var chats = await db.Chats.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
            var status = new List<object>();
            foreach (var chat in chats)
            {
                var last = await db.IncomingMessages.AsNoTracking().Where(x => x.ChatId == chat.Id)
                    .OrderByDescending(x => x.Id).Select(x => new { x.TelegramDateUtc, x.ReceivedAtUtc,
                        x.SenderTelegramId, x.Status, x.ParseError }).FirstOrDefaultAsync(ct);
                var lastLive = await db.IncomingMessages.AsNoTracking().Where(x => x.ChatId == chat.Id && x.Status == "processed")
                    .MaxAsync(x => (DateTimeOffset?)x.ReceivedAtUtc, ct);
                status.Add(new { chat.Id, chat.Name, chat.Enabled, chat.TelegramChatId, chat.SenderTelegramId,
                    LastMessage = last, LastProcessedLiveAtUtc = lastLive,
                    Incidents = await db.Incidents.CountAsync(x => x.ChatId == chat.Id, ct),
                    Active = await db.Incidents.CountAsync(x => x.ChatId == chat.Id && x.EndedAtUtc == null, ct) });
            }
            return Results.Ok(new {
                WebhookConfigured = webhookReady,
                PendingUpdates = webhook.TryGetProperty("pending_update_count", out var pending) ? pending.GetInt32() : 0,
                LastWebhookError = webhook.TryGetProperty("last_error_message", out var error) ? error.GetString() : null,
                LastWebhookErrorAtUtc = webhook.TryGetProperty("last_error_date", out var errorDate)
                    ? DateTimeOffset.FromUnixTimeSeconds(errorDate.GetInt64()) : (DateTimeOffset?)null,
                Chats = status
            });
        });
        api.MapPost("/admin/import-export/{chatId:int}", async (int chatId, HttpRequest req, MiniAppAuth auth,
            AdminSession sessions, AlgorithmDbContext db, IncidentService incidents, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(x => x.Id == chatId && x.Enabled, ct);
            if (chat is null) return Results.BadRequest("Оберіть активний чат із довідника.");
            if (!req.HasFormContentType) return Results.BadRequest("Надішліть файл result.json.");
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > 20 * 1024 * 1024 ||
                !file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Оберіть JSON-файл експорту одного чату розміром до 20 МБ.");
            try
            {
                await using var stream = file.OpenReadStream();
                var actor = db.AuditActorUserId;
                db.AuditActorUserId = null;
                var result = await TelegramDesktopImport.ImportAsync(stream, chat, incidents, DateTimeOffset.UtcNow, ct);
                db.AuditEvents.Add(new AuditEvent { ActorUserId = actor, Action = "import-telegram-export",
                    Entity = "SourceChat", EntityId = chat.Id,
                    Detail = $"Eligible={result.EligibleMessages}, imported={result.ImportedMessages}, duplicates={result.Duplicates}, review={result.NeedsReview}",
                    CreatedAtUtc = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct);
                return Results.Ok(result);
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentOutOfRangeException)
            {
                return Results.BadRequest(ex.Message);
            }
        });
        api.MapPut("/admin/reminder", async (ReminderEdit data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req,auth,sessions,db,ct)) return Results.StatusCode(403);
            if(data.Minutes<0 || data.Minutes>1440) return Results.BadRequest("Допустимо 0–1440 хвилин");
            var setting=await db.Settings.FindAsync(["reminder_minutes"],ct);
            if(setting is null) db.Settings.Add(new Setting { Key="reminder_minutes", Value=data.Minutes.ToString() });
            else setting.Value=data.Minutes.ToString();
            await db.SaveChangesAsync(ct);return Results.Ok();
        });
        api.MapPost("/admin/chats", async (SourceChat data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(data.Name)) return Results.BadRequest();
            db.Chats.Add(new SourceChat { TelegramChatId = data.TelegramChatId, SenderTelegramId = data.SenderTelegramId, Name = data.Name, Enabled = data.Enabled });
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPut("/admin/chats/{id:int}", async (int id, SourceChat data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value = await db.Chats.FindAsync([id], ct); if (value is null) return Results.NotFound();
            value.TelegramChatId = data.TelegramChatId; value.SenderTelegramId = data.SenderTelegramId;
            value.Name = data.Name.Trim(); value.Enabled = data.Enabled;
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPost("/admin/objects", async (MonitoredObject data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            db.Objects.Add(new MonitoredObject { Code = data.Code.Trim(), Name = data.Name.Trim(), Enabled = data.Enabled });
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPut("/admin/objects/{id:int}", async (int id, MonitoredObject data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value=await db.Objects.FindAsync([id],ct); if(value is null)return Results.NotFound();
            value.Code=data.Code.Trim();value.Name=data.Name.Trim();value.Enabled=data.Enabled;
            await db.SaveChangesAsync(ct);return Results.Ok();
        });
        api.MapPost("/admin/categories", async (ProblemCategory data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            db.Categories.Add(new ProblemCategory { Name = data.Name.Trim(), Enabled = data.Enabled });
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPut("/admin/categories/{id:int}", async (int id, ProblemCategory data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value=await db.Categories.FindAsync([id],ct);if(value is null)return Results.NotFound();
            value.Name=data.Name.Trim();value.Enabled=data.Enabled;
            await db.SaveChangesAsync(ct);return Results.Ok();
        });
        api.MapPost("/admin/algorithms", async (AlgorithmRule data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            try { _ = new Regex(data.MatchPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { return Results.BadRequest("Некоректне правило"); }
            var rule = new AlgorithmRule { ObjectId = data.ObjectId, CategoryId = data.CategoryId,
                Name = data.Name.Trim(), MatchPattern = data.MatchPattern, Priority = data.Priority, Enabled = data.Enabled };
            db.AlgorithmRules.Add(rule);
            await db.SaveChangesAsync(ct);
            await AlgorithmCatalog.AddTestTemplatesAsync(db, rule, ct);
            return Results.Ok();
        });
        api.MapPut("/admin/algorithms/{id:int}", async (int id, AlgorithmRule data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            try { _ = new Regex(data.MatchPattern,RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)); }
            catch(ArgumentException){return Results.BadRequest("Некоректне правило");}
            var value=await db.AlgorithmRules.FindAsync([id],ct);if(value is null)return Results.NotFound();
            var canonical = await AlgorithmCatalog.CanonicalIdsAsync(db, ct);
            if (!canonical.TryGetValue(id, out var canonicalId) || canonicalId != id)
                return Results.BadRequest("Редагуйте спільний тип алгоритму.");
            var aliases = await db.AlgorithmRules.Where(x => x.Id != id && x.Name == value.Name).ToListAsync(ct);
            var relatedAliasIds = aliases.Where(x => canonical.TryGetValue(x.Id, out var groupId) && groupId == id).Select(x => x.Id).ToHashSet();
            value.ObjectId=data.ObjectId;value.CategoryId=data.CategoryId;value.Name=data.Name.Trim();
            value.MatchPattern=data.MatchPattern;value.Priority=data.Priority;value.Enabled=data.Enabled;
            foreach (var alias in aliases.Where(x => relatedAliasIds.Contains(x.Id)))
            {
                alias.CategoryId = value.CategoryId; alias.Name = value.Name;
                alias.MatchPattern = value.MatchPattern; alias.Priority = value.Priority; alias.Enabled = false;
            }
            await db.SaveChangesAsync(ct);return Results.Ok();
        });
        api.MapPost("/admin/routes", async (RouteRule data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            db.RouteRules.Add(new RouteRule { ChatId = data.ChatId, ObjectId = data.ObjectId, CategoryId = data.CategoryId,
                UserId = data.UserId, Priority = data.Priority, Enabled = data.Enabled });
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPut("/admin/routes/{id:int}", async (int id, RouteRule data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value=await db.RouteRules.FindAsync([id],ct);if(value is null)return Results.NotFound();
            value.ChatId=data.ChatId;value.ObjectId=data.ObjectId;value.CategoryId=data.CategoryId;
            value.UserId=data.UserId;value.Priority=data.Priority;value.Enabled=data.Enabled;
            await db.SaveChangesAsync(ct);return Results.Ok();
        });
        api.MapPost("/admin/templates", async (ResponseTemplate data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(data.Title) || string.IsNullOrWhiteSpace(data.Text) || data.AlgorithmRuleId is null)
                return Results.BadRequest("Задайте алгоритм, назву та текст відповіді.");
            var rule = await db.AlgorithmRules.FindAsync([data.AlgorithmRuleId.Value], ct);
            if (rule is null) return Results.BadRequest("Алгоритм не знайдено.");
            var value = new ResponseTemplate { Title = data.Title.Trim(), Text = data.Text.Trim(), AlgorithmRuleId = rule.Id,
                CategoryId = rule.CategoryId, SortOrder = data.SortOrder, Enabled = data.Enabled };
            db.ResponseTemplates.Add(value);
            await db.SaveChangesAsync(ct);
            db.TemplateVersions.Add(new TemplateVersion { TemplateId = value.Id, Version = 1, Title = value.Title, Text = value.Text, SavedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPut("/admin/templates/{id:int}", async (int id, ResponseTemplate data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value = await db.ResponseTemplates.FindAsync([id], ct);
            if (value is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(data.Title) || string.IsNullOrWhiteSpace(data.Text) || data.AlgorithmRuleId is null)
                return Results.BadRequest("Задайте алгоритм, назву та текст відповіді.");
            var rule = await db.AlgorithmRules.FindAsync([data.AlgorithmRuleId.Value], ct);
            if (rule is null) return Results.BadRequest("Алгоритм не знайдено.");
            if (value.AlgorithmRuleId is not null && value.AlgorithmRuleId != rule.Id)
                return Results.BadRequest("Для іншого алгоритму створіть окремий шаблон.");
            value.Title = data.Title.Trim(); value.Text = data.Text.Trim(); value.AlgorithmRuleId = rule.Id;
            value.ObjectId = null; value.CategoryId = rule.CategoryId;
            value.SortOrder = data.SortOrder; value.Enabled = data.Enabled; value.Version++;
            db.TemplateVersions.Add(new TemplateVersion { TemplateId = id, Version = value.Version, Title = value.Title, Text = value.Text, SavedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPut("/admin/users/{id:int}", async (int id, UserEdit data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value = await db.Users.FindAsync([id], ct);
            if (value is null) return Results.NotFound();
            if (!new[] { "admin", "operator", "viewer" }.Contains(data.Role) ||
                !new[] { "pending", "approved", "denied", "blocked" }.Contains(data.Status)) return Results.BadRequest();
            value.DisplayName = data.DisplayName.Trim(); value.Role = data.Role; value.Status = data.Status;
            await db.SaveChangesAsync(ct); return Results.Ok();
        });
        api.MapPost("/admin/scopes", async (UserScope data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            if (!await db.UserScopes.AnyAsync(x => x.UserId == data.UserId && x.ObjectId == data.ObjectId, ct))
            { db.UserScopes.Add(new UserScope { UserId = data.UserId, ObjectId = data.ObjectId }); await db.SaveChangesAsync(ct); }
            return Results.Ok();
        });
        api.MapDelete("/admin/scopes/{userId:int}/{objectId:int}", async (int userId, int objectId, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            var value=await db.UserScopes.FindAsync([userId,objectId],ct);
            if(value is null)return Results.NotFound();
            db.UserScopes.Remove(value);await db.SaveChangesAsync(ct);return Results.Ok();
        });
        api.MapPost("/admin/preview", async (PreviewRequest data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct) =>
        {
            if (!await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            try
            {
                var rule = await AlgorithmCatalog.MatchExistingAsync(db, data.Text, ct);
                return Results.Ok(new { rule.Id, rule.Name, Time = EventParser.ParseTime(data.TelegramDateUtc, data.Text) });
            }
            catch (Exception ex) when (ex is FormatException or RegexMatchTimeoutException) { return Results.BadRequest(ex.Message); }
        });
        api.MapPost("/demo/message", async (DemoMessage data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, IncidentService service, IConfiguration cfg, CancellationToken ct) =>
        {
            if (cfg["Telegram:Mode"] != "Demo" || !await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            return Results.Ok(await service.IngestAsync(new InboundEvent(data.ChatTelegramId, data.SenderTelegramId,
                data.MessageTelegramId, data.TelegramDateUtc, data.Text), ct));
        });
        api.MapPost("/demo/response", async (DemoResponse data, HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, IncidentService service, IConfiguration cfg, CancellationToken ct) =>
        {
            if (cfg["Telegram:Mode"] != "Demo" || !await IsAdmin(req, auth, sessions, db, ct)) return Results.StatusCode(403);
            return Results.Ok(await service.RespondAsync(data.IncidentId, data.TelegramUserId,
                data.Text ?? "", data.TemplateId, $"demo:{Guid.NewGuid():N}", ct));
        });
    }

    private static IQueryable<Incident> Visible(AlgorithmDbContext db, AppUser user) => user.Role == "admin"
        ? db.Incidents : db.Incidents.Where(x => db.UserScopes.Any(s => s.UserId == user.Id && s.ObjectId == x.ObjectId));

    internal static async Task<bool> IsAdmin(HttpRequest req, MiniAppAuth auth, AdminSession sessions, AlgorithmDbContext db, CancellationToken ct)
    {
        var user = await auth.GetUserAsync(req, db, ct);
        var authorized = user?.Role == "admin" && user.Status == "approved" && sessions.Valid(req, user.TelegramId);
        if (authorized) db.AuditActorUserId = user!.Id;
        return authorized;
    }

    private sealed record SignIn(string Password);
    private sealed record UserEdit(string DisplayName, string Role, string Status);
    private sealed record PreviewRequest(string Text, DateTimeOffset TelegramDateUtc);
    private sealed record DemoMessage(long ChatTelegramId, long SenderTelegramId, int MessageTelegramId, DateTimeOffset TelegramDateUtc, string Text);
    private sealed record DemoResponse(long IncidentId, long TelegramUserId, int? TemplateId, string? Text);
    private sealed record ResponseEdit(int? TemplateId, string? Text, string? RequestId);
    private sealed record ReminderEdit(int Minutes);
}
