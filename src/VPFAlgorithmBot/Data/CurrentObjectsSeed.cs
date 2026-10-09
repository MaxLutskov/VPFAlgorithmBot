using Microsoft.EntityFrameworkCore;
using VPFAlgorithmBot.Domain;

namespace VPFAlgorithmBot.Data;

// The current physical sites supplied by operations. Historical incidents keep their original object IDs.
public static class CurrentObjectsSeed
{
    public const string Marker = "catalog:current-objects:2026-10-09:v1";
    public static readonly string[] Names = [
        "ВФС", "НС", "ПНС-1", "ПНС-2", "ПНС-3",
        .. Enumerable.Range(1, 14).Select(i => $"РЧВ {i}"),
        "РЧВ 22", "РЧВ 42", "РЧВ 43", "РЧВ 47", "РЧВ 49", "РЧВ ІПС"
    ];

    public static Task<int> ApplyAsync(AlgorithmDbContext db, CancellationToken ct = default) =>
        DatabaseWork.RunAsync(db, async () =>
        {
            if (await db.Settings.AnyAsync(x => x.Key == Marker, ct)) return 0;
            var objects = await db.Objects.ToListAsync(ct);
            var oldRchvIds = objects.Where(x => AlgorithmCatalog.Family(x.Code) == "РЧВ")
                .Select(x => x.Id).ToHashSet();
            var active = new Dictionary<string, MonitoredObject>(StringComparer.Ordinal);
            var created = 0;
            foreach (var name in Names)
            {
                var code = EventParser.NormalizeObjectCode(name);
                var matches = objects.Where(x => EventParser.NormalizeObjectCode(x.Code) == code)
                    .OrderByDescending(x => x.Enabled).ThenBy(x => x.Id).ToArray();
                var chosen = matches.FirstOrDefault();
                if (chosen is null)
                {
                    chosen = new MonitoredObject { Code = code, Name = name };
                    db.Objects.Add(chosen);
                    objects.Add(chosen);
                    created++;
                }
                chosen.Name = name;
                chosen.Enabled = true;
                active.Add(code, chosen);
                foreach (var alias in matches.Skip(1)) alias.Enabled = false;
            }
            await db.SaveChangesAsync(ct);

            var activeIds = active.Values.Select(x => x.Id).ToHashSet();
            foreach (var obj in objects.Where(x => !activeIds.Contains(x.Id))) obj.Enabled = false;

            // Workbook rules can be anchored to generic family placeholders. Keep their IDs and answers,
            // but attach them to a current object so the algorithm remains editable in the admin panel.
            var anchorByFamily = active.Values.GroupBy(x => AlgorithmCatalog.Family(x.Code))
                .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Id).First().Id);
            var ipsId = active[EventParser.NormalizeObjectCode("РЧВ ІПС")].Id;
            foreach (var rule in await db.AlgorithmRules.ToListAsync(ct))
            {
                var old = objects.First(x => x.Id == rule.ObjectId);
                if (old.Enabled) continue;
                if (EventParser.NormalizeObjectCode(old.Code) == "ІПС") rule.ObjectId = ipsId;
                else if (anchorByFamily.TryGetValue(AlgorithmCatalog.Family(old.Code), out var anchor)) rule.ObjectId = anchor;
            }

            int[] Replacements(MonitoredObject old)
            {
                var code = EventParser.NormalizeObjectCode(old.Code);
                if (active.TryGetValue(code, out var exact)) return [exact.Id];
                if (code == "ІПС") return [ipsId];
                if (code is "РЧВ" or "ПНС") return active.Values
                    .Where(x => AlgorithmCatalog.Family(x.Code) == code).Select(x => x.Id).ToArray();
                return [];
            }
            var routes = await db.RouteRules.ToListAsync(ct);
            var scopes = await db.UserScopes.ToListAsync(ct);
            var scopeKeys = scopes.Select(x => (x.UserId, x.ObjectId)).ToHashSet();
            foreach (var route in routes.Where(x => x.ObjectId is not null).ToArray())
            {
                var old = objects.First(x => x.Id == route.ObjectId);
                if (old.Enabled) continue;
                var replacements = Replacements(old);
                if (replacements.Length == 0) continue;
                if (route.Enabled) foreach (var objectId in replacements)
                {
                    if (!routes.Any(x => x.UserId == route.UserId && x.ObjectId == objectId && x.Enabled))
                    {
                        var replacement = new RouteRule { UserId = route.UserId, ObjectId = objectId,
                            ChatId = route.ChatId, CategoryId = route.CategoryId, Priority = route.Priority };
                        db.RouteRules.Add(replacement);
                        routes.Add(replacement);
                    }
                    if (scopeKeys.Add((route.UserId, objectId))) db.UserScopes.Add(new UserScope { UserId = route.UserId, ObjectId = objectId });
                }
                db.RouteRules.Remove(route);
            }
            foreach (var scope in scopes)
            {
                var old = objects.First(x => x.Id == scope.ObjectId);
                if (old.Enabled) continue;
                var replacements = Replacements(old);
                if (replacements.Length == 0) continue;
                foreach (var objectId in replacements)
                    if (scopeKeys.Add((scope.UserId, objectId))) db.UserScopes.Add(new UserScope { UserId = scope.UserId, ObjectId = objectId });
                db.UserScopes.Remove(scope);
            }

            var chats = await db.Chats.ToDictionaryAsync(x => x.Id, ct);
            var rchvIds = active.Values.Where(x => AlgorithmCatalog.Family(x.Code) == "РЧВ")
                .Select(x => x.Id).OrderBy(x => x).ToArray();
            foreach (var schedule in await db.DailyDigestSchedules.ToListAsync(ct))
            {
                if (!chats.TryGetValue(schedule.ChatId, out var chat) ||
                    !chat.Name.Equals("Алгоритми ВДВП", StringComparison.OrdinalIgnoreCase)) continue;
                var selected = Telegram.DailyDigestService.ObjectIds(schedule.ObjectIdsCsv);
                if (selected.Length > 0 && selected.All(oldRchvIds.Contains))
                    schedule.ObjectIdsCsv = string.Join(',', rchvIds);
            }

            db.Settings.Add(new Setting { Key = Marker, Value = string.Join(',', Names) });
            await db.SaveChangesAsync(ct);
            return created;
        }, ct);
}
