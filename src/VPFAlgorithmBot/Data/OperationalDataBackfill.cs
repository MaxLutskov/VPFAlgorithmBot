using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Data;

public static class OperationalDataBackfill
{
    public static Task<int> ApplyAsync(AlgorithmDbContext db, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            var updated = 0;
            while (true)
            {
                var incidents = await db.Incidents.Where(x => x.OperationalData == null)
                    .OrderBy(x => x.Id).Take(200).ToListAsync(ct);
                if (incidents.Count == 0) break;
                var ids = incidents.Select(x => x.StartMessageId).ToArray();
                var messages = await db.IncomingMessages.AsNoTracking().Where(x => ids.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct);
                foreach (var incident in incidents)
                    incident.OperationalData = messages.TryGetValue(incident.StartMessageId, out var message) &&
                        EventParser.Kind(message.Text) == "problem" ? EventParser.OperationalData(message.Text) : "";
                await db.SaveChangesAsync(ct);
                updated += incidents.Count;
            }
            return updated;
        }, ct);
}
