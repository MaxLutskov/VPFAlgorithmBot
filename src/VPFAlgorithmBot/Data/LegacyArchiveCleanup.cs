using Microsoft.EntityFrameworkCore;

namespace VPFAlgorithmBot.Data;

public static class LegacyArchiveCleanup
{
    public const string Marker = "cleanup:legacy-archive:v1";
    private static readonly long[] ArchiveChatIds = [-8_000_000_000_001L, -8_000_000_000_002L];

    public static Task<int> ApplyAsync(AlgorithmDbContext db, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            if (await db.Settings.AnyAsync(x => x.Key == Marker, ct)) return 0;
            var chats = await db.Chats.Where(x => ArchiveChatIds.Contains(x.TelegramChatId)).ToListAsync(ct);
            var chatIds = chats.Select(x => x.Id).ToArray();
            var incidents = await db.Incidents.Where(x => chatIds.Contains(x.ChatId)).ToListAsync(ct);
            var incidentIds = incidents.Select(x => x.Id).ToArray();
            db.NotificationOutbox.RemoveRange(await db.NotificationOutbox.Where(x => incidentIds.Contains(x.IncidentId)).ToListAsync(ct));
            db.Responses.RemoveRange(await db.Responses.Where(x => incidentIds.Contains(x.IncidentId)).ToListAsync(ct));
            db.Incidents.RemoveRange(incidents);
            await db.SaveChangesAsync(ct);
            db.IncomingMessages.RemoveRange(await db.IncomingMessages.Where(x => chatIds.Contains(x.ChatId)).ToListAsync(ct));
            db.RouteRules.RemoveRange(await db.RouteRules.Where(x => x.ChatId != null && chatIds.Contains(x.ChatId.Value)).ToListAsync(ct));
            db.Chats.RemoveRange(chats);
            db.Settings.Add(new Setting { Key = Marker, Value = incidents.Count.ToString() });
            db.AuditEvents.Add(new AuditEvent { Action = "remove-legacy-archive", Entity = "Incident",
                Detail = $"Removed {incidents.Count} legacy archive incidents", CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            return incidents.Count;
        }, ct);
}
