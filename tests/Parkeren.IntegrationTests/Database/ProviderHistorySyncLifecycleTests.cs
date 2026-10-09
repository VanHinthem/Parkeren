using Microsoft.EntityFrameworkCore;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderHistorySyncLifecycleTests(PostgreSqlFixture fixture)
{
    private sealed class Reader(Func<int, CancellationToken, Task<ProviderActionHistoryPage>> read)
        : IProviderActionHistoryReader
    {
        public Task<ProviderActionHistoryPage> GetActionHistoryPageAsync(
            string productId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
            read(pageNumber, cancellationToken);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task Full_run_persists_terminal_outcome(string outcome)
    {
        var token = TestContext.Current.CancellationToken;
        var product = $"lifecycle-{Guid.NewGuid():N}";
        var reader = new Reader((number, ct) => outcome switch
        {
            "failure" => throw new InvalidOperationException("History reader failed."),
            "cancel" => throw new OperationCanceledException(ct),
            _ => Task.FromResult(new ProviderActionHistoryPage([], number, 10, 0))
        });

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var service = CreateService(db, reader);
                if (outcome == "success")
                    await service.ImportAsync(product, 10, token);
                else if (outcome == "failure")
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        service.ImportAsync(product, 10, token));
                else
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                        service.ImportAsync(product, 10, token));
            }

            await using (var check = fixture.CreateDbContext())
            {
                var run = await check.ProviderHistorySyncRuns.AsNoTracking()
                    .SingleAsync(x => x.ProviderProductId == product, token);
                var expected = outcome switch
                {
                    "success" => ProviderHistorySyncRunStatus.Succeeded,
                    "failure" => ProviderHistorySyncRunStatus.Failed,
                    _ => ProviderHistorySyncRunStatus.Cancelled
                };
                Assert.Equal(expected, run.Status);
                Assert.NotNull(run.FinishedAt);
                Assert.Equal(outcome == "failure" ? "History reader failed." : null, run.Error);
                Assert.Equal(0, run.ReadCount);
                var checkpoint = await check.ProviderHistorySyncStates.AsNoTracking()
                    .SingleAsync(x => x.ProviderProductId == product, token);
                Assert.Equal(0, checkpoint.NextPageNumber);
                if (outcome == "success")
                    Assert.NotNull(checkpoint.LastSuccessfulSyncAt);
                else
                    Assert.Null(checkpoint.LastSuccessfulSyncAt);
                if (outcome == "failure")
                    Assert.Equal("History reader failed.", checkpoint.LastError);
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserved_run_executor_reuses_the_run_and_records_its_outcome(bool fail)
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"reserved-executor-{Guid.NewGuid():N}";
        var reader = new Reader((page, token) => fail
            ? throw new InvalidOperationException("Reserved history read failed.")
            : Task.FromResult(new ProviderActionHistoryPage([], page, 10, 0)));

        try
        {
            Guid runId;
            await using (var reserve = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(reserve, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                runId = run.Id;
            }

            await using (var db = fixture.CreateDbContext())
            {
                var executor = new ProviderHistoryReservedRunExecutor(
                    db, CreateService(db, reader),
                    new ProviderHistorySyncRunStore(db), TimeProvider.System);
                if (fail)
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        executor.ExecuteAsync(runId, ct));
                else
                    await executor.ExecuteAsync(runId, ct);
            }

            await using var verify = fixture.CreateDbContext();
            var runs = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.ProviderProductId == product).ToListAsync(ct);
            var saved = Assert.Single(runs);
            Assert.Equal(runId, saved.Id);
            Assert.Equal(ProviderHistorySyncRunMode.Manual, saved.Mode);
            Assert.Equal(fail ? ProviderHistorySyncRunStatus.Failed :
                ProviderHistorySyncRunStatus.Succeeded, saved.Status);
            Assert.NotNull(saved.FinishedAt);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Completed_manual_import_can_be_requested_again_without_creating_duplicate_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"manual-repeat-{Guid.NewGuid():N}";
        var readPages = new List<int>();
        var reader = new Reader((page, token) =>
        {
            readPages.Add(page);
            return Task.FromResult(new ProviderActionHistoryPage([], page, 10, 0));
        });

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Guid runId;
                await using (var reserve = fixture.CreateDbContext())
                {
                    var run = await new ProviderHistorySyncRunStarter(reserve, TimeProvider.System)
                        .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                    Assert.NotNull(run);
                    runId = run.Id;
                }

                await using var execution = fixture.CreateDbContext();
                await new ProviderHistoryReservedRunExecutor(
                    execution, CreateService(execution, reader),
                    new ProviderHistorySyncRunStore(execution), TimeProvider.System)
                    .ExecuteAsync(runId, ct);
            }

            Assert.Equal([0, 0], readPages);
            await using var verify = fixture.CreateDbContext();
            var runs = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.ProviderProductId == product).ToListAsync(ct);
            Assert.Equal(2, runs.Count);
            Assert.All(runs, run =>
            {
                Assert.Equal(ProviderHistorySyncRunMode.Manual, run.Mode);
                Assert.Equal(ProviderHistorySyncRunStatus.Succeeded, run.Status);
            });
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Incremental_reread_restarts_at_first_page_and_corrects_older_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"overlap-{suffix}";
        var plate = $"OL{suffix[..6].ToUpperInvariant()}";
        var start = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var firstId = $"first-overlap-{suffix}";
        var secondId = $"second-overlap-{suffix}";
        var corrected = false;
        var requestedPages = new List<int>();
        var reader = new Reader((page, _) =>
        {
            requestedPages.Add(page);
            var record = page switch
            {
                0 => new ProviderActionHistoryRecord(firstId, "COMPLETED",
                    start, start.AddMinutes(corrected ? 25 : 20),
                    corrected ? 0.35m : 0.25m, "EUR", plate),
                1 => new ProviderActionHistoryRecord(secondId, "COMPLETED",
                    start.AddHours(1), start.AddHours(1).AddMinutes(20),
                    0.30m, "EUR", plate),
                _ => throw new InvalidOperationException("Unexpected history page.")
            };
            return Task.FromResult(new ProviderActionHistoryPage([record], page, 1, 2));
        });

        try
        {
            await using (var db = fixture.CreateDbContext())
            {
                var initial = await CreateService(db, reader).ImportAsync(product, 1, ct);
                Assert.Equal(2, initial.Inserted);
            }

            corrected = true;
            await using (var db = fixture.CreateDbContext())
            {
                var updated = await CreateService(db, reader).ImportAsync(product, 1, ct);
                Assert.Equal(0, updated.Inserted);
                Assert.Equal(2, updated.Refreshed);
            }

            Assert.Equal([0, 1, 0, 1], requestedPages);
            await using var verify = fixture.CreateDbContext();
            var first = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.ProviderActionId == firstId, ct);
            Assert.Equal(start.AddMinutes(25), first.ActualEndAt);
            Assert.Equal(0.35m, first.ProviderCostAmount);
            Assert.Equal(2, await verify.ProviderParkingActions.CountAsync(
                x => x.ProviderActionId == firstId || x.ProviderActionId == secondId, ct));
            Assert.Equal(0, await verify.ProviderHistorySyncStates
                .Where(x => x.ProviderProductId == product)
                .Select(x => x.NextPageNumber).SingleAsync(ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions.Where(x =>
                x.ProviderActionId == firstId || x.ProviderActionId == secondId)
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task End_to_end_reimport_preserves_manual_assignment_and_applies_provider_correction()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var product = $"e2e-history-{suffix}";
        var providerId = $"e2e-action-{suffix}";
        var plate = $"E2{suffix[..6].ToUpperInvariant()}";
        var start = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var actor = new User(Guid.NewGuid(), $"e2e-admin-{suffix}",
            $"E2E-ADMIN-{suffix}", "hash", UserRole.Admin);
        var assignee = new User(Guid.NewGuid(), $"e2e-visitor-{suffix}",
            $"E2E-VISITOR-{suffix}", "hash", UserRole.Visitor);
        var revised = false;
        var reader = new Reader((page, _) =>
        {
            Assert.Equal(0, page);
            var record = new ProviderActionHistoryRecord(providerId, "COMPLETED",
                start, start.AddMinutes(revised ? 35 : 30),
                revised ? 0.45m : 0.35m, "EUR", plate, "OSS_J");
            return Task.FromResult(new ProviderActionHistoryPage([record], 0, 10, 1));
        });

        // Reimport and provider-side corrections must not produce visits or
        // retrospective notification events, inbox items, or push deliveries.
        int initialVisits;
        int initialEvents;
        int initialNotifications;
        int initialPushDeliveries;
        await using (var baseline = fixture.CreateDbContext())
        {
            initialVisits = await baseline.Visits.CountAsync(ct);
            initialEvents = await baseline.NotificationEvents.CountAsync(ct);
            initialNotifications = await baseline.Notifications.CountAsync(ct);
            initialPushDeliveries = await baseline.PushDeliveries.CountAsync(ct);
        }

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.AddRange(actor, assignee);
                await seed.SaveChangesAsync(ct);
            }

            Guid actionId;
            await using (var initial = fixture.CreateDbContext())
            {
                var result = await CreateService(initial, reader).ImportAsync(product, 10, ct);
                Assert.Equal(1, result.Inserted);
                Assert.Equal(0, result.Refreshed);
                actionId = await initial.ProviderParkingActions
                    .Where(x => x.ProviderActionId == providerId)
                    .Select(x => x.Id).SingleAsync(ct);
            }

            await using (var assignment = fixture.CreateDbContext())
            {
                Assert.True(await new ProviderHistoryAssignmentService(assignment, TimeProvider.System)
                    .AssignAsync(actionId, actor.Id, assignee.Id, ct));
            }

            await using (var repeat = fixture.CreateDbContext())
            {
                var result = await CreateService(repeat, reader).ImportAsync(product, 10, ct);
                Assert.Equal(0, result.Inserted);
                Assert.Equal(1, result.Refreshed);
            }

            revised = true;
            await using (var correction = fixture.CreateDbContext())
            {
                var result = await CreateService(correction, reader).ImportAsync(product, 10, ct);
                Assert.Equal(0, result.Inserted);
                Assert.Equal(1, result.Refreshed);
            }

            await using var verify = fixture.CreateDbContext();
            var action = await verify.ProviderParkingActions.AsNoTracking()
                .SingleAsync(x => x.Id == actionId, ct);
            Assert.Equal(ProviderActionOrigin.Imported, action.Origin);
            Assert.Null(action.VisitId);
            Assert.Equal(assignee.Id, action.AssignedUserId);
            Assert.Equal(ProviderActionAssignmentSource.ManuallyAssigned, action.AssignmentSource);
            Assert.Equal(start.AddMinutes(35), action.ActualEndAt);
            Assert.Equal(0.45m, action.ProviderCostAmount);
            Assert.Equal(1, await verify.ProviderParkingActions.CountAsync(
                x => x.ProviderActionId == providerId, ct));
            Assert.Equal(1, await verify.AdminAuditEvents.CountAsync(
                x => x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                     x.TargetId == actionId.ToString("D"), ct));
            Assert.Equal(initialVisits, await verify.Visits.CountAsync(ct));
            Assert.Equal(initialEvents, await verify.NotificationEvents.CountAsync(ct));
            Assert.Equal(initialNotifications, await verify.Notifications.CountAsync(ct));
            Assert.Equal(initialPushDeliveries, await verify.PushDeliveries.CountAsync(ct));
            Assert.Equal(3, await verify.ProviderHistorySyncRuns.CountAsync(
                x => x.ProviderProductId == product &&
                     x.Status == ProviderHistorySyncRunStatus.Succeeded, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            var actionIds = await cleanup.ProviderParkingActions
                .Where(x => x.ProviderActionId == providerId)
                .Select(x => x.Id).ToListAsync(ct);
            foreach (var id in actionIds)
                await cleanup.AdminAuditEvents.Where(x =>
                    x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                    x.TargetId == id.ToString("D")).ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.ProviderActionId == providerId)
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == actor.Id || x.Id == assignee.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Shutdown_cancellation_preserves_reserved_run_for_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"shutdown-resume-{Guid.NewGuid():N}";
        var cancelledReader = new Reader((_, token) => throw new OperationCanceledException(token));
        try
        {
            Guid runId;
            await using (var reserve = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(reserve, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                runId = run.Id;
            }

            await using (var interrupted = fixture.CreateDbContext())
            {
                var executor = new ProviderHistoryReservedRunExecutor(
                    interrupted, CreateService(interrupted, cancelledReader),
                    new ProviderHistorySyncRunStore(interrupted), TimeProvider.System);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    executor.ExecuteAsync(runId, ct));
            }

            await using (var verify = fixture.CreateDbContext())
            {
                var run = await verify.ProviderHistorySyncRuns.AsNoTracking()
                    .SingleAsync(x => x.Id == runId, ct);
                Assert.Equal(ProviderHistorySyncRunStatus.Running, run.Status);
                Assert.Null(run.FinishedAt);
            }

            await using (var restarted = fixture.CreateDbContext())
            {
                var reader = new Reader((page, token) =>
                    Task.FromResult(new ProviderActionHistoryPage([], page, 10, 0)));
                var executor = new ProviderHistoryReservedRunExecutor(
                    restarted, CreateService(restarted, reader),
                    new ProviderHistorySyncRunStore(restarted), TimeProvider.System);
                await executor.ExecuteAsync(runId, ct);
            }

            await using var completed = fixture.CreateDbContext();
            Assert.Equal(ProviderHistorySyncRunStatus.Succeeded,
                await completed.ProviderHistorySyncRuns.AsNoTracking()
                    .Where(x => x.Id == runId).Select(x => x.Status).SingleAsync(ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Reserved_run_restarts_at_page_zero_after_worker_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = $"restart-sync-{Guid.NewGuid():N}";
        var readPages = new List<int>();
        var reader = new Reader((page, token) =>
        {
            readPages.Add(page);
            return Task.FromResult(new ProviderActionHistoryPage([], page, 10, 0));
        });

        try
        {
            Guid runId;
            await using (var initial = fixture.CreateDbContext())
            {
                var run = await new ProviderHistorySyncRunStarter(initial, TimeProvider.System)
                    .TryStartAsync(product, ProviderHistorySyncRunMode.Manual, ct);
                Assert.NotNull(run);
                runId = run.Id;

                var checkpoints = new ProviderHistorySyncStateStore(initial);
                await checkpoints.GetOrCreateAsync(product, 10, ct);
                await checkpoints.RecordPageCompletedAsync(product, 0, TimeProvider.System.GetUtcNow(), ct);
                // Simulate a process crash after committing page 0 but before
                // marking the reserved run completed.
            }

            await using (var restarted = fixture.CreateDbContext())
            {
                var executor = new ProviderHistoryReservedRunExecutor(
                    restarted, CreateService(restarted, reader),
                    new ProviderHistorySyncRunStore(restarted), TimeProvider.System);
                await executor.ExecuteAsync(runId, ct);
            }

            // Replay from zero prevents missing records when provider indices shift.
            Assert.Equal([0], readPages);
            await using var verify = fixture.CreateDbContext();
            var runStatus = await verify.ProviderHistorySyncRuns.AsNoTracking()
                .Where(x => x.Id == runId).Select(x => x.Status).SingleAsync(ct);
            Assert.Equal(ProviderHistorySyncRunStatus.Succeeded, runStatus);
            var checkpoint = await verify.ProviderHistorySyncStates.AsNoTracking()
                .SingleAsync(x => x.ProviderProductId == product, ct);
            Assert.Equal(0, checkpoint.NextPageNumber);
            Assert.NotNull(checkpoint.LastSuccessfulSyncAt);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == product)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Successful_history_import_records_budget_baseline_without_notifications()
    {
        var token = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var externalProduct = $"budget-history-{suffix}";
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var product = new ParkingProviderProduct(Guid.NewGuid(), externalProduct,
            "History budget", "TEST", "Test", $"LOC-{suffix}", start);
        var period = new ParkingBudgetPeriod(Guid.NewGuid(),
            start.AddDays(-1), start.AddDays(1), TimeSpan.FromHours(1));
        period.AssignProviderProduct(product.Id);
        var rules = new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-2), null,
            TimeSpan.FromHours(4),
            [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))]);
        rules.AssignProviderProduct(product.Id);
        var plate = $"BH{suffix[..6].ToUpperInvariant()}";
        var actionId = $"budget-action-{suffix}";
        var corrected = false;
        var record = new ProviderActionHistoryRecord(actionId, "COMPLETED",
            start, start.AddHours(1), 0.20m, "EUR", plate);
        var reader = new Reader((page, ct) =>
            Task.FromResult(new ProviderActionHistoryPage(
                page == 0 ? [corrected ? record with { ActualEndAt = start.AddMinutes(90), ProviderCostAmount = 0.30m } : record] : [], page, 10, 1)));
        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ParkingBudgetPeriods.Add(period);
                seed.ParkingRuleSets.Add(rules);
                await seed.SaveChangesAsync(token);
            }

            int eventCountBefore;
            int inboxCountBefore;
            int pushCountBefore;
            await using (var before = fixture.CreateDbContext())
            {
                eventCountBefore = await before.NotificationEvents.CountAsync(token);
                inboxCountBefore = await before.Notifications.CountAsync(token);
                pushCountBefore = await before.PushDeliveries.CountAsync(token);
            }

            async Task AssertNoRetrospectiveNotificationsAsync()
            {
                await using var check = fixture.CreateDbContext();
                Assert.Equal(eventCountBefore, await check.NotificationEvents.CountAsync(token));
                Assert.Equal(inboxCountBefore, await check.Notifications.CountAsync(token));
                Assert.Equal(pushCountBefore, await check.PushDeliveries.CountAsync(token));
            }

            await using (var db = fixture.CreateDbContext())
                await CreateService(db, reader).ImportAsync(externalProduct, 10, token);

            await using (var verify = fixture.CreateDbContext())
            {
                Assert.True(await verify.ProviderParkingActions.AnyAsync(
                    x => x.ProviderActionId == actionId && x.VisitId == null, token));
                var thresholds = await verify.ParkingBudgetWarningStates.AsNoTracking()
                    .Where(x => x.ParkingBudgetPeriodId == period.Id)
                    .Select(x => x.ThresholdPercentage).ToArrayAsync(token);
                Assert.NotEmpty(thresholds);
                await AssertNoRetrospectiveNotificationsAsync();
            }

            // Replaying the same history must not duplicate budget thresholds
            // or generate previously suppressed notifications.
            int firstBaselineCount;
            await using (var baseline = fixture.CreateDbContext())
                firstBaselineCount = await baseline.ParkingBudgetWarningStates.CountAsync(
                    x => x.ParkingBudgetPeriodId == period.Id, token);

            await using (var repeat = fixture.CreateDbContext())
                await CreateService(repeat, reader).ImportAsync(externalProduct, 10, token);

            await using (var repeated = fixture.CreateDbContext())
            {
                Assert.Equal(firstBaselineCount, await repeated.ParkingBudgetWarningStates
                    .CountAsync(x => x.ParkingBudgetPeriodId == period.Id, token));
                await AssertNoRetrospectiveNotificationsAsync();
            }

            // A later provider correction updates realized paid minutes without
            // duplicate warning baselines or retrospective notifications.
            corrected = true;
            await using (var correction = fixture.CreateDbContext())
                await CreateService(correction, reader).ImportAsync(externalProduct, 10, token);

            await using (var verifiedCorrection = fixture.CreateDbContext())
            {
                var action = await verifiedCorrection.ProviderParkingActions.AsNoTracking()
                    .SingleAsync(x => x.ProviderActionId == actionId, token);
                var usage = RealizedParkingBudgetUsageCalculator.CalculateFromActions(
                    period, [action], [rules]);
                Assert.Equal(TimeSpan.FromMinutes(90), usage.UsedPaidDuration);
                Assert.Equal(0.30m, action.ProviderCostAmount);
                Assert.Equal(firstBaselineCount, await verifiedCorrection.ParkingBudgetWarningStates
                    .CountAsync(x => x.ParkingBudgetPeriodId == period.Id, token));
                await AssertNoRetrospectiveNotificationsAsync();
                Assert.Equal(1, await verifiedCorrection.ProviderParkingActions
                    .CountAsync(x => x.ProviderActionId == actionId, token));
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingBudgetWarningStates.Where(x => x.ParkingBudgetPeriodId == period.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncRuns.Where(x => x.ProviderProductId == externalProduct)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderHistorySyncStates.Where(x => x.ProviderProductId == externalProduct)
                .ExecuteDeleteAsync(token);
            await cleanup.ProviderParkingActions.Where(x => x.ProviderActionId == actionId)
                .ExecuteDeleteAsync(token);
            await cleanup.Vehicles.Where(x => x.NormalizedLicensePlate == plate)
                .ExecuteDeleteAsync(token);
            await cleanup.PaidWindows.Where(x => x.ParkingRuleSetId == rules.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ParkingRuleSets.Where(x => x.Id == rules.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ParkingBudgetPeriods.Where(x => x.Id == period.Id)
                .ExecuteDeleteAsync(token);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_budget_baseline_rejects_missing_or_partial_rules(bool partial)
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var externalId = $"rules-{suffix}";
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var product = new ParkingProviderProduct(Guid.NewGuid(), externalId,
            "Budget test", "TEST", "Test", $"LOC-{suffix}", start);
        var period = new ParkingBudgetPeriod(Guid.NewGuid(), start.AddDays(-1),
            start.AddDays(1), TimeSpan.FromHours(1));
        period.AssignProviderProduct(product.Id);
        var plate = $"RR{suffix[..6].ToUpperInvariant()}";
        var vehicle = Parkeren.Domain.Vehicles.Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
        var action = Parkeren.Domain.Visits.ProviderParkingAction.ImportCompleted(
            Guid.NewGuid(), $"action-{suffix}", externalId, product.Location,
            vehicle.Id, Parkeren.Domain.Visits.ProviderActionAssignment.Unassigned,
            start, start.AddHours(1), 0.20m, "COMPLETED", start.AddHours(2));
        ParkingRuleSet? rules = null;
        if (partial)
        {
            rules = new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1),
                start.AddMinutes(30), TimeSpan.FromHours(4),
                [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))]);
            rules.AssignProviderProduct(product.Id);
        }

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ParkingBudgetPeriods.Add(period);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                if (rules is not null)
                    seed.ParkingRuleSets.Add(rules);
                await seed.SaveChangesAsync(ct);
            }

            await using (var db = fixture.CreateDbContext())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    ProviderHistoryBudgetBaseline.RecordAsync(db, externalId, start.AddHours(3), ct));
                Assert.Contains("Missing historical parking rules", error.Message);
            }

            await using var check = fixture.CreateDbContext();
            Assert.False(await check.ParkingBudgetWarningStates
                .AnyAsync(x => x.ParkingBudgetPeriodId == period.Id, ct));
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingBudgetWarningStates.Where(x => x.ParkingBudgetPeriodId == period.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(ct);
            if (rules is not null)
            {
                await cleanup.PaidWindows.Where(x => x.ParkingRuleSetId == rules.Id)
                    .ExecuteDeleteAsync(ct);
                await cleanup.ParkingRuleSets.Where(x => x.Id == rules.Id)
                    .ExecuteDeleteAsync(ct);
            }
            await cleanup.ParkingBudgetPeriods.Where(x => x.Id == period.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    private static ProviderHistoryCheckpointedImportService CreateService(
        ParkerenDbContext db, IProviderActionHistoryReader reader)
    {
        var checkpoint = new ProviderHistorySyncStateStore(db);
        var pages = new ProviderHistoryTransactionalPageImporter(
            db, new ProviderHistoryPageImporter(
                new ProviderHistoryExistingActionStore(db),
                new ProviderHistoryNewActionStore(db)), checkpoint,
            new ProviderHistorySyncRunStore(db));
        return new ProviderHistoryCheckpointedImportService(
            reader, pages, checkpoint, TimeProvider.System, db,
            new ProviderHistorySyncRunStore(db));
    }
}
