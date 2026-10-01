using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class UsageAnalysisAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Shared_plate_keeps_usage_attributed_to_each_visitor_and_archived_entities_visible()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAnalysisStateAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor1 = new User(Guid.NewGuid(), $"visitor-a-{suffix}", $"VISITOR-A-{suffix}", "hash", UserRole.Visitor);
        var visitor2 = new User(Guid.NewGuid(), $"visitor-b-{suffix}", $"VISITOR-B-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), "12-AB-34", "12AB34", "Gedeeld");

        var offset = TimeSpan.FromHours(2);
        var firstStart = new DateTimeOffset(2026, 9, 28, 10, 0, 0, offset).ToUniversalTime();
        var firstEnd = new DateTimeOffset(2026, 9, 28, 11, 0, 0, offset).ToUniversalTime();
        var secondStart = firstEnd;
        var secondEnd = new DateTimeOffset(2026, 9, 28, 12, 30, 0, offset).ToUniversalTime();

        var firstVisit = CompletedVisit(visitor1.Id, vehicle.Id, firstStart, firstEnd);
        var secondVisit = CompletedVisit(visitor2.Id, vehicle.Id, secondStart, secondEnd);

        visitor1.Deactivate();
        vehicle.Deactivate();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor1, visitor2);
            seed.Vehicles.Add(vehicle);
            seed.Visits.AddRange(firstVisit, secondVisit);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            await using var administration = CreateAdministration();
            var analysis = await administration.Service.GetUsageAnalysisAsync(
                admin.Id,
                firstStart.AddMinutes(-1),
                secondEnd.AddMinutes(1),
                ct);

            Assert.Equal(2, analysis.ByUser.Count);

            var archivedVisitor = Assert.Single(analysis.ByUser, x => x.UserId == visitor1.Id);
            Assert.True(archivedVisitor.IsArchived);
            Assert.Equal(1, archivedVisitor.VisitCount);
            Assert.Equal(60, archivedVisitor.PaidDurationMinutes);
            Assert.Equal(2m, archivedVisitor.Amount);
            Assert.Single(archivedVisitor.Visits);
            Assert.Equal(firstVisit.Id, archivedVisitor.Visits[0].VisitId);

            var activeVisitor = Assert.Single(analysis.ByUser, x => x.UserId == visitor2.Id);
            Assert.False(activeVisitor.IsArchived);
            Assert.Equal(1, activeVisitor.VisitCount);
            Assert.Equal(90, activeVisitor.PaidDurationMinutes);
            Assert.Equal(3m, activeVisitor.Amount);
            Assert.Single(activeVisitor.Visits);
            Assert.Equal(secondVisit.Id, activeVisitor.Visits[0].VisitId);

            var plate = Assert.Single(analysis.ByLicensePlate);
            Assert.Equal("12AB34", plate.Label);
            Assert.True(plate.IsArchived);
            Assert.Equal(2, plate.VisitCount);
            Assert.Equal(150, plate.PaidDurationMinutes);
            Assert.Equal(5m, plate.Amount);
            Assert.Equal(2, plate.Visits.Count);
            Assert.Contains(plate.Visits, x => x.UserId == visitor1.Id);
            Assert.Contains(plate.Visits, x => x.UserId == visitor2.Id);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Visits.Where(x => x.Id == firstVisit.Id || x.Id == secondVisit.Id).ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitor1.Id || x.Id == visitor2.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingTariffs.ExecuteDeleteAsync(ct);
        }
    }

    private static Visit CompletedVisit(
        Guid userId,
        Guid vehicleId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            userId,
            vehicleId,
            userId,
            start,
            end,
            new EffectiveParkingPolicySnapshot(null, null, true, false));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(end);
        return visit;
    }

    private async Task ResetAnalysisStateAsync(CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await context.ProviderOperations.ExecuteDeleteAsync(ct);
        await context.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await context.Visits.ExecuteDeleteAsync(ct);
        await context.ParkingTariffs.ExecuteDeleteAsync(ct);
        await context.PaidWindows.ExecuteDeleteAsync(ct);
        await context.ParkingCalendarExceptions.ExecuteDeleteAsync(ct);
        await context.ParkingRuleSets.ExecuteDeleteAsync(ct);

        var paidWindows = Enumerable.Range((int)DayOfWeek.Monday, 6)
            .Select(day => new PaidWindow(
                (DayOfWeek)day,
                new TimeOnly(9, 0),
                new TimeOnly(20, 0)))
            .ToArray();

        context.ParkingRuleSets.Add(new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            validUntil: null,
            TimeSpan.FromHours(4),
            paidWindows,
            publicHolidaysAreFree: true,
            continuation: ProviderCoverageContinuation.StartNewAction));

        context.ParkingTariffs.Add(new ParkingTariff(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            validUntil: null,
            rate: 2m,
            unit: ParkingTariffUnit.Hour));

        await context.SaveChangesAsync(ct);
    }

    private AdministrationScope CreateAdministration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        return new AdministrationScope(provider, scope, scope.ServiceProvider.GetRequiredService<IAdministrationService>());
    }

    private sealed class AdministrationScope(
        ServiceProvider provider,
        AsyncServiceScope scope,
        IAdministrationService service) : IAsyncDisposable
    {
        public IAdministrationService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
