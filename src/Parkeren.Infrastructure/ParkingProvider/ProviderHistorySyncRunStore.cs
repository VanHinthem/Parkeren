using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.ParkingProvider;

/// <summary>
/// Persists individual history synchronization attempts and their outcomes.
/// </summary>
public sealed class ProviderHistorySyncRunStore(ParkerenDbContext db)
{
    public async Task<Guid> StartAsync(string providerProductId,
        ProviderHistorySyncRunMode mode, DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        var run = new ProviderHistorySyncRun(Guid.NewGuid(), providerProductId, mode, startedAt);
        db.ProviderHistorySyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task RecordPageAsync(Guid runId, int read, int inserted,
        int refreshed, int skipped, CancellationToken cancellationToken = default)
    {
        var run = await FindAsync(runId, cancellationToken);
        run.RecordPage(read, inserted, refreshed, skipped);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid runId, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var run = await FindAsync(runId, cancellationToken);
        run.Complete(at);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid runId, DateTimeOffset at, string? error,
        CancellationToken cancellationToken = default)
    {
        var run = await FindAsync(runId, cancellationToken);
        run.Fail(at, error);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(Guid runId, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var run = await FindAsync(runId, cancellationToken);
        run.Cancel(at);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<ProviderHistorySyncRun> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.ProviderHistorySyncRuns.SingleAsync(x => x.Id == id, cancellationToken);
}
