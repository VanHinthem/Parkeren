using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistoryCheckpointResumeTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Resume_after_failed_page_preserves_prior_commit_and_skips_it()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"resume-{suffix}";
        var plate = $"RS{suffix[..6].ToUpperInvariant()}";
        var start = DateTimeOffset.UnixEpoch.AddDays(20000);
        var first = new ProviderActionHistoryRecord($"first-{suffix}", "COMPLETED",
            start, start.AddMinutes(10), 0.2m, "EUR", plate);
        var second = first with { ProviderActionId = $"second-{suffix}", ActualStartAt = start.AddMinutes(20),
            ActualEndAt = start.AddMinutes(30) };
        var bad = second with { Status = "ACTIVE" };

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var handler = CreateImporter(db);
                await handler.ImportPageAsync(product,
                    new ProviderActionHistoryPage([first], 0, 1, 2), start.AddHours(1), token);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    handler.ImportPageAsync(product,
                        new ProviderActionHistoryPage([bad], 1, 1, 2), start.AddHours(1), token));
            }

            await using (var db = fixture.CreateDbContext())
            {
                var progress = await new ProviderHistorySyncStateStore(db)
                    .GetOrCreateAsync(product, 1, token);
                Assert.Equal(1, progress.NextPageNumber);
                Assert.Equal(1, await db.ProviderParkingActions.CountAsync(
                    x => x.ProviderActionId == first.ProviderActionId, token));
            }

            await using (var db = fixture.CreateDbContext())
            {
                var handler = CreateImporter(db);
                var summary = await handler.ImportPageAsync(product,
                    new ProviderActionHistoryPage([second], 1, 1, 2), start.AddHours(2), token);
                Assert.Equal(1, summary.Inserted);
            }

            await using (var db = fixture.CreateDbContext())
            {
                Assert.Equal(2, await db.ProviderParkingActions.CountAsync(
                    x => x.ProviderActionId == first.ProviderActionId ||
                         x.ProviderActionId == second.ProviderActionId, token));
                Assert.Equal(2, await db.ProviderHistorySyncStates
                    .Where(x => x.ProviderProductId == product)
                    .Select(x => x.NextPageNumber).SingleAsync(token));
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.ProviderParkingActions.Where(x =>
                x.ProviderActionId == first.ProviderActionId ||
                x.ProviderActionId == second.ProviderActionId).ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
            await db.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Resume_imports_new_terminal_page_when_provider_history_grows()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"resume-grow-{suffix}";
        var plate = $"RG{suffix[..6].ToUpperInvariant()}";
        var start = DateTimeOffset.UnixEpoch.AddDays(20000);
        var records = Enumerable.Range(0, 3).Select(index =>
            new ProviderActionHistoryRecord($"grow-{index}-{suffix}", "COMPLETED",
                start.AddMinutes(index * 20), start.AddMinutes(index * 20 + 10),
                0.2m, "EUR", plate)).ToArray();

        try
        {
            // The first run commits page zero while the provider has two actions.
            await using (var db = fixture.CreateDbContext())
            {
                await CreateImporter(db).ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[0]], 0, 1, 2),
                    start.AddHours(1), token);
            }

            // After interruption, the provider adds a third action. The saved
            // checkpoint stays at page one; subsequent pages observe total three.
            await using (var db = fixture.CreateDbContext())
            {
                var checkpoint = await new ProviderHistorySyncStateStore(db)
                    .GetOrCreateAsync(product, 1, token);
                Assert.Equal(1, checkpoint.NextPageNumber);
                await CreateImporter(db).ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[1]], 1, 1, 3),
                    start.AddHours(2), token);
                await CreateImporter(db).ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[2]], 2, 1, 3),
                    start.AddHours(2), token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                Assert.Equal(3, await db.ProviderParkingActions.CountAsync(
                    x => records.Select(record => record.ProviderActionId)
                        .Contains(x.ProviderActionId), token));
                var progress = await new ProviderHistorySyncStateStore(db)
                    .GetOrCreateAsync(product, 1, token);
                Assert.Equal(3, progress.NextPageNumber);
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            var ids = records.Select(record => record.ProviderActionId).ToArray();
            await db.ProviderParkingActions.Where(x => ids.Contains(x.ProviderActionId))
                .ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
            await db.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    [Fact]
    public async Task Shifted_indices_on_resume_are_reconciled_by_next_full_sync()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"shift-resume-{suffix}";
        var plate = $"SR{suffix[..6].ToUpperInvariant()}";
        var start = DateTimeOffset.UnixEpoch.AddDays(20000);
        var records = Enumerable.Range(0, 3).Select(index =>
            new ProviderActionHistoryRecord($"shift-{index}-{suffix}", "COMPLETED",
                start.AddMinutes(index * 20), start.AddMinutes(index * 20 + 10),
                0.2m, "EUR", plate)).ToArray();

        try
        {
            // Original provider order: A, B. Page zero (A) commits.
            await using (var db = fixture.CreateDbContext())
            {
                await CreateImporter(db).ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[0]], 0, 1, 2),
                    start.AddHours(1), token);
            }

            // A newly inserted leading action shifts the provider order to C, A, B.
            // Resuming at page one sees A again, then B; C is not visited.
            await using (var db = fixture.CreateDbContext())
            {
                var importer = CreateImporter(db);
                await importer.ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[0]], 1, 1, 3),
                    start.AddHours(2), token);
                await importer.ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[1]], 2, 1, 3),
                    start.AddHours(2), token);
                Assert.Equal(2, await db.ProviderParkingActions.CountAsync(
                    x => x.ProviderProductId == product, token));

                // A completed run resets the page checkpoint, making a later full
                // reread possible. The resumed run alone cannot detect the gap.
                await new ProviderHistorySyncStateStore(db)
                    .RecordSyncCompletedAsync(product, start.AddHours(2), token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var checkpoint = await new ProviderHistorySyncStateStore(db)
                    .GetOrCreateAsync(product, 1, token);
                Assert.Equal(0, checkpoint.NextPageNumber);

                var importer = CreateImporter(db);
                await importer.ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[2]], 0, 1, 3),
                    start.AddHours(3), token);
                await importer.ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[0]], 1, 1, 3),
                    start.AddHours(3), token);
                await importer.ImportPageAsync(product,
                    new ProviderActionHistoryPage([records[1]], 2, 1, 3),
                    start.AddHours(3), token);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var ids = records.Select(record => record.ProviderActionId).ToArray();
                Assert.Equal(3, await db.ProviderParkingActions.CountAsync(
                    x => ids.Contains(x.ProviderActionId), token));
            }
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            var ids = records.Select(record => record.ProviderActionId).ToArray();
            await db.ProviderParkingActions.Where(x => ids.Contains(x.ProviderActionId))
                .ExecuteDeleteAsync(token);
            await db.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
            await db.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    private static ProviderHistoryTransactionalPageImporter CreateImporter(
        Parkeren.Infrastructure.Persistence.ParkerenDbContext db) =>
        new(db, new ProviderHistoryPageImporter(
            new ProviderHistoryExistingActionStore(db),
            new ProviderHistoryNewActionStore(db)),
            new ProviderHistorySyncStateStore(db));
}
