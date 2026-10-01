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
public sealed class BudgetTariffAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Overlapping_budget_period_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        var from = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Guid? createdId = null;

        try
        {
            await using var administration = CreateAdministration();
            var first = await administration.Service.CreateBudgetPeriodAsync(
                admin.Id, from, from.AddYears(1), 1500 * 60, ct);
            Assert.Equal(AdminBudgetPeriodCreateOutcome.Created, first.Outcome);
            createdId = first.Period!.Id;

            var overlapping = await administration.Service.CreateBudgetPeriodAsync(
                admin.Id, from.AddMonths(6), from.AddYears(1).AddMonths(6), 1500 * 60, ct);

            Assert.Equal(AdminBudgetPeriodCreateOutcome.Overlap, overlapping.Outcome);
        }
        finally
        {
            await CleanupAsync(admin.Id, createdId is null ? [] : [createdId.Value], [], ct);
        }
    }

    [Fact]
    public async Task New_open_tariff_closes_previous_open_tariff()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        var firstFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var secondFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var tariffIds = new List<Guid>();

        try
        {
            await using var administration = CreateAdministration();
            var first = await administration.Service.CreateParkingTariffAsync(
                admin.Id, firstFrom, null, 1m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, first.Outcome);
            tariffIds.Add(first.Tariff!.Id);

            var second = await administration.Service.CreateParkingTariffAsync(
                admin.Id, secondFrom, null, 2m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, second.Outcome);
            tariffIds.Add(second.Tariff!.Id);

            await using var verify = fixture.CreateDbContext();
            var persistedFirst = await verify.ParkingTariffs.SingleAsync(x => x.Id == first.Tariff.Id, ct);
            var persistedSecond = await verify.ParkingTariffs.SingleAsync(x => x.Id == second.Tariff.Id, ct);

            Assert.Equal(persistedSecond.ValidFrom, persistedFirst.ValidUntil);
            Assert.Null(persistedSecond.ValidUntil);
        }
        finally
        {
            await CleanupAsync(admin.Id, [], tariffIds, ct);
        }
    }

    [Fact]
    public async Task Budget_usage_counts_only_paid_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var localOffset = TimeSpan.FromHours(2);
        var periodFrom = new DateTimeOffset(2026, 9, 28, 0, 0, 0, localOffset).ToUniversalTime();
        var periodUntil = new DateTimeOffset(2026, 9, 29, 0, 0, 0, localOffset).ToUniversalTime();
        Guid? budgetId = null;
        CompletedVisitSeed? visitSeed = null;

        try
        {
            visitSeed = await CreateCompletedVisitAsync(
                new DateTimeOffset(2026, 9, 28, 8, 0, 0, localOffset).ToUniversalTime(),
                new DateTimeOffset(2026, 9, 28, 10, 0, 0, localOffset).ToUniversalTime(),
                ct);
            await using var administration = CreateAdministration();
            var budget = await administration.Service.CreateBudgetPeriodAsync(
                admin.Id, periodFrom, periodUntil, 10 * 60, ct);
            Assert.Equal(AdminBudgetPeriodCreateOutcome.Created, budget.Outcome);
            budgetId = budget.Period!.Id;

            var usage = await administration.Service.GetBudgetUsageAsync(admin.Id, budgetId, periodFrom.AddHours(12), ct);

            Assert.NotNull(usage);
            Assert.True(usage.IsComplete);
            Assert.Equal(60, usage.UsedPaidDurationMinutes);
            Assert.Equal(540, usage.RemainingPaidDurationMinutes);

        }
        finally
        {
            if (visitSeed is not null)
                await CleanupVisitorAsync(visitSeed.UserId, visitSeed.VehicleId, ct);
            await CleanupAsync(admin.Id, budgetId is null ? [] : [budgetId.Value], [], ct);
        }
    }

    [Fact]
    public async Task Cost_report_splits_paid_visit_across_tariff_versions()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var localOffset = TimeSpan.FromHours(2);
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, localOffset).ToUniversalTime();
        var boundary = new DateTimeOffset(2026, 9, 28, 11, 0, 0, localOffset).ToUniversalTime();
        var end = new DateTimeOffset(2026, 9, 28, 12, 0, 0, localOffset).ToUniversalTime();
        var tariffIds = new List<Guid>();
        CompletedVisitSeed? visitSeed = null;

        try
        {
            visitSeed = await CreateCompletedVisitAsync(start, end, ct);

            await using var administration = CreateAdministration();
            var first = await administration.Service.CreateParkingTariffAsync(
                admin.Id, start.AddHours(-1), boundary, 1m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, first.Outcome);
            tariffIds.Add(first.Tariff!.Id);

            var second = await administration.Service.CreateParkingTariffAsync(
                admin.Id, boundary, end.AddHours(1), 2m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, second.Outcome);
            tariffIds.Add(second.Tariff!.Id);

            var report = await administration.Service.GetCostReportAsync(
                admin.Id, start.AddMinutes(-1), end.AddMinutes(1), ct);

            Assert.True(report.IsComplete);
            Assert.Equal(120, report.TotalPaidDurationMinutes);
            Assert.Equal(3m, report.TotalAmount);
            var visit = Assert.Single(report.Visits);
            Assert.Equal(120, visit.PaidDurationMinutes);
            Assert.Equal(3m, visit.Amount);
            Assert.True(visit.IsComplete);
        }
        finally
        {
            if (visitSeed is not null)
                await CleanupVisitorAsync(visitSeed.UserId, visitSeed.VehicleId, ct);
            await CleanupAsync(admin.Id, [], tariffIds, ct);
        }
    }

    [Fact]
    public async Task Cost_report_is_incomplete_when_paid_time_has_no_historical_tariff()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var localOffset = TimeSpan.FromHours(2);
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, localOffset).ToUniversalTime();
        var end = new DateTimeOffset(2026, 9, 28, 11, 0, 0, localOffset).ToUniversalTime();
        CompletedVisitSeed? visitSeed = null;

        try
        {
            visitSeed = await CreateCompletedVisitAsync(start, end, ct);
            await using var administration = CreateAdministration();

            var report = await administration.Service.GetCostReportAsync(
                admin.Id,
                start.AddMinutes(-1),
                end.AddMinutes(1),
                ct);

            Assert.False(report.IsComplete);
            Assert.Null(report.TotalAmount);
            var visit = Assert.Single(report.Visits);
            Assert.Equal(60, visit.PaidDurationMinutes);
            Assert.Null(visit.Amount);
            Assert.False(visit.IsComplete);
        }
        finally
        {
            if (visitSeed is not null)
                await CleanupVisitorAsync(visitSeed.UserId, visitSeed.VehicleId, ct);
            await CleanupAsync(admin.Id, [], [], ct);
        }
    }

    private async Task ResetCalculationStateAsync(CancellationToken ct)
    {
        await ClearVisitsAsync(ct);

        await using var context = fixture.CreateDbContext();
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

        await context.SaveChangesAsync(ct);
    }

    private async Task<User> CreateAdminAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);
        await using var context = fixture.CreateDbContext();
        context.Users.Add(admin);
        await context.SaveChangesAsync(ct);
        return admin;
    }

    private async Task<CompletedVisitSeed> CreateCompletedVisitAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var visitor = new User(Guid.NewGuid(), $"visitor-{suffix}", $"VISITOR-{suffix}", "hash", UserRole.Visitor);
        var plate = $"BT{suffix}"[..8];
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);
        var visit = new Visit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            visitor.Id,
            vehicle.Id,
            visitor.Id,
            start,
            end,
            new EffectiveParkingPolicySnapshot(null, null, true, false));
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(end);

        await using var context = fixture.CreateDbContext();
        context.Users.Add(visitor);
        context.Vehicles.Add(vehicle);
        context.Visits.Add(visit);
        await context.SaveChangesAsync(ct);
        return new CompletedVisitSeed(visitor.Id, vehicle.Id);
    }

    private async Task CleanupVisitorAsync(Guid userId, Guid vehicleId, CancellationToken ct)
    {
        await ClearVisitsAsync(ct);
        await using var context = fixture.CreateDbContext();
        await context.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(ct);
        await context.Vehicles.Where(x => x.Id == vehicleId).ExecuteDeleteAsync(ct);
    }

    private async Task ClearVisitsAsync(CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await context.ProviderOperations.ExecuteDeleteAsync(ct);
        await context.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await context.Visits.ExecuteDeleteAsync(ct);
    }

    private async Task CleanupAsync(
        Guid adminId,
        IReadOnlyCollection<Guid> budgetIds,
        IReadOnlyCollection<Guid> tariffIds,
        CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        if (budgetIds.Count > 0)
            await context.ParkingBudgetPeriods.Where(x => budgetIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        if (tariffIds.Count > 0)
            await context.ParkingTariffs.Where(x => tariffIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await context.AdminAuditEvents.Where(x => x.ActorUserId == adminId).ExecuteDeleteAsync(ct);
        await context.Users.Where(x => x.Id == adminId).ExecuteDeleteAsync(ct);
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

    private sealed record CompletedVisitSeed(Guid UserId, Guid VehicleId);

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
