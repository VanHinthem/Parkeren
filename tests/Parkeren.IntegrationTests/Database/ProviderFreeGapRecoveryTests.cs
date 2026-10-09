using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderFreeGapRecoveryTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData("scheduled", false, false)]
    [InlineData("scheduled", true, false)]
    [InlineData("active", false, false)]
    [InlineData("active", true, false)]
    [InlineData("missing", false, false)]
    [InlineData("stopped", false, false)]
    [InlineData("start-mismatch", false, false)]
    [InlineData("end-mismatch", false, false)]
    [InlineData("scheduled", true, true)] // Open-ended Visit survives a free gap and keeps future continuation.
    public async Task Recovery_rebuilds_scheduler_for_scheduled_successor_after_free_gap(
        string providerReadback,
        bool includeFurtherPaidPeriod,
        bool openEnded)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        // Keep the synthetic parking windows in daytime, even when CI runs at night.
        // Use a future date so recovery does not treat scheduled work as overdue.
        var now = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(9), TimeSpan.Zero);
        (await http.PostAsJsonAsync(
            "api/test/clock/set",
            new { UtcNow = now },
            cancellationToken)).EnsureSuccessStatusCode();
        var nextPaidStart = now.AddMinutes(4);
        var firstPaidEnd = nextPaidStart.AddHours(-1);
        var nextPaidEnd = nextPaidStart.AddHours(1);
        var furtherPaidStart = includeFurtherPaidPeriod ? nextPaidEnd.AddHours(1) : (DateTimeOffset?)null;
        var furtherPaidEnd = furtherPaidStart?.AddHours(1);
        var startAt = firstPaidEnd.AddHours(-1);
        var desiredEndAt = (furtherPaidEnd ?? nextPaidEnd).AddHours(1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"gap-recovery-{suffix}", $"GAP-RECOVERY-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"GR-{suffix[..2]}-{suffix[2..4]}", $"GR{suffix[..4]}", null);

        ParkingProviderProduct product;
        var createdProduct = false;
        await using (var productContext = fixture.CreateDbContext())
        {
            product = await productContext.ParkingProviderProducts
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.ProviderProductId == "visitor", cancellationToken)
                ?? new ParkingProviderProduct(
                    Guid.NewGuid(),
                    "visitor",
                    "Bezoekersparkeren",
                    "oss",
                    "Oss",
                    "OSS_J",
                    now);

            if (!await productContext.ParkingProviderProducts.AnyAsync(x => x.Id == product.Id, cancellationToken))
            {
                productContext.ParkingProviderProducts.Add(product);
                await productContext.SaveChangesAsync(cancellationToken);
                createdProduct = true;
            }
        }

        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            openEnded ? null : desiredEndAt,
            openEnded
                ? new EffectiveParkingPolicySnapshot(null, null, true)
                : new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true),
            product.Id,
            product.ProviderProductId,
            product.Location);
        visit.Activate();

        var remotePredecessor = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(
                vehicle.NormalizedLicensePlate,
                startAt,
                firstPaidEnd,
                "Oss",
                product.ProviderProductId),
            cancellationToken);

        var predecessor = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(),
            visit.Id,
            startAt,
            firstPaidEnd,
            product.ProviderProductId,
            product.Location);
        predecessor.MarkStarting();
        predecessor.MarkActive(remotePredecessor.ProviderActionId, startAt, remotePredecessor.Status);

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            nextPaidStart.AddMinutes(-5));
        work.Claim("free-gap-recovery-test", now);

        var businessZone = TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId);
        var paidWindows = CreatePaidWindows(nextPaidStart, nextPaidEnd, businessZone);

        var paidBeforeGap = new ParkingRuleSet(
            Guid.NewGuid(), startAt.AddDays(-1), firstPaidEnd, TimeSpan.FromHours(4), paidWindows);
        var freeGap = new ParkingRuleSet(
            Guid.NewGuid(), firstPaidEnd, nextPaidStart, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());
        var paidAfterGap = new ParkingRuleSet(
            Guid.NewGuid(), nextPaidStart, nextPaidEnd, TimeSpan.FromHours(4), paidWindows);
        var freeTail = new ParkingRuleSet(
            Guid.NewGuid(), nextPaidEnd, furtherPaidStart, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());
        ParkingRuleSet? furtherPaid = null;
        ParkingRuleSet? finalFreeTail = null;
        if (furtherPaidStart is DateTimeOffset paidStart && furtherPaidEnd is DateTimeOffset paidEnd)
        {
            furtherPaid = new ParkingRuleSet(
                Guid.NewGuid(), paidStart, paidEnd, TimeSpan.FromHours(4),
                CreatePaidWindows(paidStart, paidEnd, businessZone));
            finalFreeTail = new ParkingRuleSet(
                Guid.NewGuid(), paidEnd, null, TimeSpan.FromHours(4), Array.Empty<PaidWindow>());
        }
        paidBeforeGap.AssignProviderProduct(product.Id);
        freeGap.AssignProviderProduct(product.Id);
        paidAfterGap.AssignProviderProduct(product.Id);
        freeTail.AssignProviderProduct(product.Id);
        furtherPaid?.AssignProviderProduct(product.Id);
        finalFreeTail?.AssignProviderProduct(product.Id);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(predecessor);
            seedContext.VisitSchedulerWork.Add(work);
            seedContext.ParkingRuleSets.AddRange(
                new[] { paidBeforeGap, freeGap, paidAfterGap, freeTail }
                    .Concat(new[] { furtherPaid, finalFreeTail }.OfType<ParkingRuleSet>()));
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parkeren"] = fixture.ConnectionString,
                ["ParkingProvider:Location"] = "Oss"
            });
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration);
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
            services.AddSingleton<IParkingProvider>(parkingProvider);
            services.AddLogging();
            await using var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
                var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
                await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                    .ProcessAsync(claimed, cancellationToken);
            }

            Guid successorId;
            string successorProviderActionId;
            await using (var verifyContext = fixture.CreateDbContext())
            {
                var actions = await verifyContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .OrderBy(x => x.PlannedStartAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(2, actions.Count);
                Assert.Equal(ProviderActionState.Completed, actions[0].State);
                var startOperation = await verifyContext.ProviderOperations.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.ProviderParkingActionId == actions[1].Id, cancellationToken);
                Assert.True(actions[1].State == ProviderActionState.Scheduled,
                    $"Expected Scheduled, got {actions[1].State}; operation status: {startOperation?.Status}, error: {startOperation?.LastErrorCode}, action health: {actions[1].Health}.");
                Assert.Equal(ProviderHistoryStatus.Pending, actions[0].HistoryStatus);
                var historyWork = await verifyContext.VisitSchedulerWork
                    .SingleAsync(x => x.ProviderParkingActionId == actions[0].Id, cancellationToken);
                Assert.Equal(VisitSchedulerWorkType.ReconcileProviderAction, historyWork.Type);
                Assert.InRange(
                    (historyWork.DueAt - firstPaidEnd.AddMinutes(2)).Duration(),
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(1));
                Assert.Equal(nextPaidStart.ToUnixTimeSeconds(), actions[1].PlannedStartAt.ToUnixTimeSeconds());
                successorId = actions[1].Id;
                successorProviderActionId = actions[1].ProviderActionId!;
            }

            Assert.Equal(2, (await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken)).Count);

            if (providerReadback == "active")
            {
                (await http.PostAsJsonAsync(
                    "api/test/clock/set",
                    new { UtcNow = nextPaidStart },
                    cancellationToken)).EnsureSuccessStatusCode();
            }
            else if (providerReadback == "missing")
            {
                (await http.PostAsJsonAsync(
                    "api/test/post-end-behavior",
                    new { Behavior = "hide" },
                    cancellationToken)).EnsureSuccessStatusCode();
                (await http.PostAsJsonAsync(
                    "api/test/clock/set",
                    new { UtcNow = nextPaidEnd.AddMinutes(1) },
                    cancellationToken)).EnsureSuccessStatusCode();
            }
            else if (providerReadback == "stopped")
            {
                (await http.PostAsync(
                    $"api/test/actions/{successorProviderActionId}/stop",
                    null,
                    cancellationToken)).EnsureSuccessStatusCode();
            }
            else if (providerReadback is "start-mismatch" or "end-mismatch")
            {
                (await http.PostAsJsonAsync(
                    "api/test/readback-offsets",
                    new
                    {
                        StartMilliseconds = providerReadback == "start-mismatch" ? 60_000 : 0,
                        EndMilliseconds = providerReadback == "end-mismatch" ? 60_000 : 0
                    },
                    cancellationToken)).EnsureSuccessStatusCode();
            }

            await using (var recoveryScope = provider.CreateAsyncScope())
            {
                await recoveryScope.ServiceProvider.GetRequiredService<IVisitRecoveryService>()
                    .RecoverAsync(cancellationToken);
                await recoveryScope.ServiceProvider.GetRequiredService<IVisitTerminalRecoveryService>()
                    .RecoverAsync(cancellationToken);
            }

            await using (var recoveryVerifyContext = fixture.CreateDbContext())
            {
                var actionsAfterRecovery = await recoveryVerifyContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .OrderBy(x => x.PlannedStartAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(2, actionsAfterRecovery.Count);
                Assert.Equal(successorId, actionsAfterRecovery[1].Id);
                Assert.Equal(
                    providerReadback == "active" ? ProviderActionState.Active : ProviderActionState.Scheduled,
                    actionsAfterRecovery[1].State);
                Assert.Equal(ProviderActionHealth.Healthy, actionsAfterRecovery[1].Health);

                var recoveredVisit = await recoveryVerifyContext.Visits
                    .SingleAsync(x => x.Id == visit.Id, cancellationToken);
                Assert.Equal(
                    providerReadback is "scheduled" or "active"
                        ? VisitHealth.Healthy
                        : VisitHealth.AttentionRequired,
                    recoveredVisit.Health);

                var workItems = await recoveryVerifyContext.VisitSchedulerWork
                    .Where(x => x.VisitId == visit.Id)
                    .ToListAsync(cancellationToken);
                Assert.Equal(VisitSchedulerWorkStatus.Completed,
                    workItems.Single(x => x.Id == work.Id).Status);
                var terminalWorks = workItems.Where(x =>
                    x.Type == VisitSchedulerWorkType.StopVisit &&
                    x.Status == VisitSchedulerWorkStatus.Pending).ToArray();
                if (openEnded)
                {
                    Assert.Empty(terminalWorks);
                    Assert.Equal(VisitStatus.Active, recoveredVisit.Status);
                    Assert.Null(recoveredVisit.DesiredEndAt);
                }
                else
                {
                    var terminalWork = Assert.Single(terminalWorks);
                    Assert.Equal(VisitEndReason.DesiredEndReached, terminalWork.EndReason);
                    Assert.Equal(desiredEndAt, terminalWork.DueAt);
                }
                var continuationWork = workItems.Where(x =>
                    x.Type == VisitSchedulerWorkType.ContinueProviderCoverage &&
                    (x.Status == VisitSchedulerWorkStatus.Pending || x.Status == VisitSchedulerWorkStatus.Claimed)).ToArray();
                if (providerReadback is "scheduled" or "active" && includeFurtherPaidPeriod)
                {
                    var rebuiltContinuation = Assert.Single(continuationWork);
                    Assert.Equal(furtherPaidStart!.Value.AddMinutes(-5), rebuiltContinuation.DueAt);
                }
                else
                {
                    Assert.Empty(continuationWork);
                }
            }

            Assert.Equal(
                providerReadback == "missing" ? 0 : 2,
                (await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken)).Count);
        }
        finally
        {
            await using var cleanupContext = fixture.CreateDbContext();
            await cleanupContext.ProviderOperations
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.VisitSchedulerWork
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ProviderParkingActions
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Notifications
                .Where(x => x.VisitId == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.DeleteVisitSchedulerAuditEventsAsync(cancellationToken, visit.Id);
            await cleanupContext.Visits
                .Where(x => x.Id == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ParkingRuleSets
                .Where(x => x.Id == paidBeforeGap.Id || x.Id == freeGap.Id || x.Id == paidAfterGap.Id || x.Id == freeTail.Id ||
                            (furtherPaid != null && x.Id == furtherPaid.Id) ||
                            (finalFreeTail != null && x.Id == finalFreeTail.Id))
                .ExecuteDeleteAsync(cancellationToken);
            if (createdProduct)
            {
                await cleanupContext.ParkingProviderProducts
                    .Where(x => x.Id == product.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            await cleanupContext.Vehicles
                .Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Users
                .Where(x => x.Id == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static PaidWindow[] CreatePaidWindows(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        TimeZoneInfo businessZone)
    {
        var localStart = TimeZoneInfo.ConvertTime(startAt, businessZone);
        var localEnd = TimeZoneInfo.ConvertTime(endAt, businessZone);
        return localStart.Date == localEnd.Date
            ?
            [new PaidWindow(
                localStart.DayOfWeek,
                TimeOnly.FromDateTime(localStart.DateTime),
                TimeOnly.FromDateTime(localEnd.DateTime))]
            :
            [
                new PaidWindow(
                    localStart.DayOfWeek,
                    TimeOnly.FromDateTime(localStart.DateTime),
                    TimeOnly.MaxValue),
                new PaidWindow(
                    localEnd.DayOfWeek,
                    TimeOnly.MinValue,
                    TimeOnly.FromDateTime(localEnd.DateTime))
            ];
    }
}
