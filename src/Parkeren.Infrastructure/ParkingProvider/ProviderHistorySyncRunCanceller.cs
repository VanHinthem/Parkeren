using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Cancels a reserved run only while no worker holds its execution claim.
/// Active imports cannot be interrupted safely by changing their run status.
/// </summary>
public sealed class ProviderHistorySyncRunCanceller(ParkerenDbContext db, TimeProvider clock)
{
    public async Task<bool?> TryCancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var acquired = false;
        try
        {
            acquired = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(hashtextextended({0}, 2)) AS \"Value\"",
                runId.ToString("D")).SingleAsync(cancellationToken);
            if (!acquired)
                return false;

            var run = await db.ProviderHistorySyncRuns
                .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
            if (run is null)
                return null;
            if (run.Status != ProviderHistorySyncRunStatus.Running)
                return false;

            run.Cancel(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            if (acquired)
                await db.Database.SqlQueryRaw<bool>(
                    "SELECT pg_advisory_unlock(hashtextextended({0}, 2)) AS \"Value\"",
                    runId.ToString("D")).SingleAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
