using System.Data;
using Microsoft.EntityFrameworkCore;

namespace VPFAlgorithmBot.Data;

public static class DatabaseWork
{
    // Retry the whole transaction, as required by SQL Server's retrying execution strategy.
    // A transaction-owned lock serializes catalog discovery and incident pairing across instances.
    public static async Task<T> RunAsync<T>(AlgorithmDbContext db, Func<Task<T>> action, CancellationToken ct = default)
    {
        var attempt = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0) db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsInMemory() ? null
                : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync("""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource = N'vpfalgo:catalog-incidents',
                        @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                    IF @result < 0 THROW 51000, 'Cannot acquire algorithm transaction lock.', 1;
                    """, ct);
            var result = await action();
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        });
    }
}
