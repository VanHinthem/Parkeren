using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.ParkingProvider;
using Parkeren.Application.Visits;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderJitContinuationProcessorTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Scheduler_creates_one_scheduled_successor_inside_jit_window()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mockFactory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = mockFactory.CreateClient();
        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        var parkingProvider = new TwoParkMockProvider(http);

        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();
        var boundary = now.AddMinutes(4);
        var startAt = boundary.AddHours(-4);
        var desiredEndAt = boundary.AddHours(2);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(Guid.NewGuid(), $"jit-processor-{suffix}", $"JIT-PROCESSOR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"JP-{suffix[..2]}-{suffix[2..4]}", $"JP{suffix[..4]}", null);
        ParkingProviderProduct product;
        await using (var productContext = fixture.CreateDbContext())
        {
            product = await productContext.ParkingProviderProducts
                .AsNoTracking()
                .SingleAsync(x => x.ProviderProductId == "visitor", cancellationToken);
        }
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            user.Id,
            vehicle.Id,
            user.Id,
            startAt,
            desiredEndAt,
            new EffectiveParkingPolicySnapshot(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true),
            product.Id,
            product.ProviderProductId,
            product.Location);
        visit.Activate();

        var remotePredecessor = await parkingProvider.StartActionAsync(
            new ProviderParkingActionRequest(vehicle.NormalizedLicensePlate, startAt, boundary, "Oss", product.ProviderProductId),
            cancellationToken);
        Assert.Equal("active", remotePredecessor.Status, ignoreCase: true);

        var predecessor = new Parkeren.Domain.Visits.ProviderParkingAction(
            Guid.NewGuid(), visit.Id, startAt, boundary, product.ProviderProductId, product.Location);
        predecessor.MarkStarting();
        predecessor.MarkActive(remotePredecessor.ProviderActionId, startAt, remotePredecessor.Status);

        var work = new VisitSchedulerWork(
            Guid.NewGuid(),
            visit.Id,
            VisitSchedulerWorkType.ContinueProviderCoverage,
            boundary.AddMinutes(-5));
        work.Claim("jit-continuation-test", now);

        var ruleSet = new ParkingRuleSet(
            Guid.NewGuid(),
            startAt.AddDays(-1),
            desiredEndAt.AddDays(1),
            TimeSpan.FromHours(4),
            Enumerable.Range(0, 7)
                .Select(day => new PaidWindow((DayOfWeek)day, TimeOnly.MinValue, new TimeOnly(23, 59, 59)))
                .ToArray());
        ruleSet.AssignProviderProduct(product.Id);

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Visits.Add(visit);
            seedContext.ProviderParkingActions.Add(predecessor);
            seedContext.VisitSchedulerWork.Add(work);
            seedContext.ParkingRuleSets.Add(ruleSet);
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

            await using (var verifyContext = fixture.CreateDbContext())
            {
                var persistedWork = await verifyContext.VisitSchedulerWork.SingleAsync(x => x.Id == work.Id, cancellationToken);
                Assert.Equal(VisitSchedulerWorkStatus.Completed, persistedWork.Status);

                var actions = await verifyContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .OrderBy(x => x.PlannedStartAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(2, actions.Count);
                var successor = actions[1];
                Assert.Equal(ProviderActionState.Scheduled, successor.State);
                Assert.Equal(boundary.AddSeconds(1), successor.PlannedStartAt);
                Assert.False(string.IsNullOrWhiteSpace(successor.ProviderActionId));

                var operation = Assert.Single(await verifyContext.ProviderOperations
                    .Where(x => x.VisitId == visit.Id && x.OperationId == work.Id)
                    .ToListAsync(cancellationToken));
                Assert.Equal(ProviderOperationType.ContinueStart, operation.Type);
                Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
            }

            var remoteActions = await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken);
            Assert.Equal(2, remoteActions.Count);
            var remoteSuccessor = Assert.Single(remoteActions, action =>
                action.ProviderActionId != remotePredecessor.ProviderActionId &&
                string.Equals(action.Status, "scheduled", StringComparison.OrdinalIgnoreCase) &&
                (action.Start - boundary.AddSeconds(1)).Duration() <= TimeSpan.FromSeconds(5));

            var advanceToSuccessorStart = remoteSuccessor.Start - now;
            (await http.PostAsJsonAsync("api/test/clock/advance",
                new { Milliseconds = advanceToSuccessorStart.TotalMilliseconds }, cancellationToken)).EnsureSuccessStatusCode();
            remoteActions = await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken);
            Assert.Equal("active",
                Assert.Single(remoteActions, action => action.ProviderActionId == remoteSuccessor.ProviderActionId).Status,
                ignoreCase: true);

            var duplicateWork = new VisitSchedulerWork(
                Guid.NewGuid(),
                visit.Id,
                VisitSchedulerWorkType.ContinueProviderCoverage,
                now);
            duplicateWork.Claim("jit-continuation-replay-test", now);
            await using (var duplicateContext = fixture.CreateDbContext())
            {
                duplicateContext.VisitSchedulerWork.Add(duplicateWork);
                await duplicateContext.SaveChangesAsync(cancellationToken);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
                var claimed = await context.VisitSchedulerWork.SingleAsync(x => x.Id == duplicateWork.Id, cancellationToken);
                await scope.ServiceProvider.GetRequiredService<IVisitSchedulerWorkProcessor>()
                    .ProcessAsync(claimed, cancellationToken);
            }

            await using (var replayVerifyContext = fixture.CreateDbContext())
            {
                Assert.Equal(2, await replayVerifyContext.ProviderParkingActions.CountAsync(
                    x => x.VisitId == visit.Id,
                    cancellationToken));
                Assert.Equal(VisitSchedulerWorkStatus.Pending,
                    (await replayVerifyContext.VisitSchedulerWork.SingleAsync(
                        x => x.Id == duplicateWork.Id,
                        cancellationToken)).Status);
                Assert.Equal(2, (await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken)).Count);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<Parkeren.Infrastructure.Persistence.ParkerenDbContext>();
                var persistedVisit = await context.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
                var stopResult = await scope.ServiceProvider.GetRequiredService<StopVisitFlow>().StopAsync(
                    new StopVisitCommand(Guid.NewGuid(), visit.Id, user.Id),
                    new StopVisitContext(new StopVisitActor(user.Id, UserRole.Visitor, true), persistedVisit),
                    cancellationToken);

                Assert.Equal(StopVisitFlowOutcome.Completed, stopResult.Outcome);
            }

            await using (var stopVerifyContext = fixture.CreateDbContext())
            {
                var stoppedVisit = await stopVerifyContext.Visits.SingleAsync(x => x.Id == visit.Id, cancellationToken);
                Assert.Equal(VisitStatus.Completed, stoppedVisit.Status);

                var stoppedActions = await stopVerifyContext.ProviderParkingActions
                    .Where(x => x.VisitId == visit.Id)
                    .OrderBy(x => x.PlannedStartAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(2, stoppedActions.Count);
                Assert.All(stoppedActions, action => Assert.Equal(ProviderActionState.Stopped, action.State));
            }

            var stoppedRemoteActions = await parkingProvider.GetActionsForProductAsync(product.ProviderProductId, cancellationToken);
            Assert.Equal(2, stoppedRemoteActions.Count);
            Assert.All(stoppedRemoteActions, action => Assert.Equal("stopped", action.Status, ignoreCase: true));
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
            await cleanupContext.Visits
                .Where(x => x.Id == visit.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.ParkingRuleSets
                .Where(x => x.Id == ruleSet.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Vehicles
                .Where(x => x.Id == vehicle.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanupContext.Users
                .Where(x => x.Id == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
