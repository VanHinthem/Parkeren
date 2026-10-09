using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryPeriodicSyncCandidateQueryTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Selects_only_imported_products_due_for_periodic_sync_without_running_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefix = $"periodic-{Guid.NewGuid():N}";
        var eligible = prefix + "-eligible";
        var neverImported = prefix + "-new";
        var recent = prefix + "-recent";
        var recentlyFailed = prefix + "-retry";
        var running = prefix + "-running";
        var now = DateTimeOffset.UtcNow;
        var old = now.AddHours(-1);

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                foreach (var product in new[] { eligible, neverImported, recent, recentlyFailed, running })
                {
                    var state = new ProviderHistorySyncState(Guid.NewGuid(), product, 10);
                    if (product != neverImported)
                    {
                        state.RecordSyncCompleted(product == recent ? now.AddMinutes(-5) : old);
                        if (product == recentlyFailed)
                            state.RecordFailure(now.AddMinutes(-5), "Temporary provider failure");
                    }
                    seed.ProviderHistorySyncStates.Add(state);
                }

                seed.ProviderHistorySyncRuns.Add(new ProviderHistorySyncRun(
                    Guid.NewGuid(), running, ProviderHistorySyncRunMode.Manual, old));
                await seed.SaveChangesAsync(ct);
            }

            await using var db = fixture.CreateDbContext();
            var candidates = await new ProviderHistoryPeriodicSyncCandidateQuery(db, TimeProvider.System)
                .GetDueProductIdsAsync(ct);
            Assert.Contains(eligible, candidates);
            Assert.DoesNotContain(neverImported, candidates);
            Assert.DoesNotContain(recent, candidates);
            Assert.DoesNotContain(recentlyFailed, candidates);
            Assert.DoesNotContain(running, candidates);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId.StartsWith(prefix))
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId.StartsWith(prefix))
                .ExecuteDeleteAsync(ct);
        }
    }
}
