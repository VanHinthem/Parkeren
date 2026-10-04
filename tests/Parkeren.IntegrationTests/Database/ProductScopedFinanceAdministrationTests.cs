using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProductScopedFinanceAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Finance_configuration_and_costs_are_isolated_per_provider_product()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetStateAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"finance-admin-{suffix}", $"FINANCE-ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitorA = new User(Guid.NewGuid(), $"finance-a-{suffix}", $"FINANCE-A-{suffix}", "hash", UserRole.Visitor);
        var visitorB = new User(Guid.NewGuid(), $"finance-b-{suffix}", $"FINANCE-B-{suffix}", "hash", UserRole.Visitor);
        var vehicleA = new Vehicle(Guid.NewGuid(), $"FA{suffix}"[..8], $"FA{suffix}"[..8], null);
        var vehicleB = new Vehicle(Guid.NewGuid(), $"FB{suffix}"[..8], $"FB{suffix}"[..8], null);
        var now = DateTimeOffset.UtcNow;
        var productA = new ParkingProviderProduct(Guid.NewGuid(), $"PRODUCT-A-{suffix}", "Product A", "CAT", "Test", "LOC_A", now);
        var productB = new ParkingProviderProduct(Guid.NewGuid(), $"PRODUCT-B-{suffix}", "Product B", "CAT", "Test", "LOC_B", now);

        var offset = TimeSpan.FromHours(2);
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, offset).ToUniversalTime();
        var end = new DateTimeOffset(2026, 9, 28, 11, 0, 0, offset).ToUniversalTime();
        var budgetFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var budgetUntil = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var rulesA = CreateRules(productA.Id);
        var rulesB = CreateRules(productB.Id);
        var visitA = CompletedVisit(visitorA.Id, vehicleA.Id, productA, start, end);
        var visitB = CompletedVisit(visitorB.Id, vehicleB.Id, productB, start, end);
        var actionA = CompletedProviderAction(visitA.Id, start, end);
        var actionB = CompletedProviderAction(visitB.Id, start, end);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitorA, visitorB);
            seed.Vehicles.AddRange(vehicleA, vehicleB);
            seed.ParkingProviderProducts.AddRange(productA, productB);
            seed.ParkingRuleSets.AddRange(rulesA, rulesB);
            seed.Visits.AddRange(visitA, visitB);
            seed.ProviderParkingActions.AddRange(actionA, actionB);
            await seed.SaveChangesAsync(ct);
        }

        var budgetIds = new List<Guid>();
        var tariffIds = new List<Guid>();
        try
        {
            await using var administration = CreateAdministration();

            var budgetA = await administration.Service.CreateBudgetPeriodForProductAsync(
                admin.Id, productA.Id, budgetFrom, budgetUntil, 1500 * 60, ct);
            var budgetB = await administration.Service.CreateBudgetPeriodForProductAsync(
                admin.Id, productB.Id, budgetFrom, budgetUntil, 1200 * 60, ct);
            Assert.Equal(AdminBudgetPeriodCreateOutcome.Created, budgetA.Outcome);
            Assert.Equal(AdminBudgetPeriodCreateOutcome.Created, budgetB.Outcome);
            budgetIds.Add(budgetA.Period!.Id);
            budgetIds.Add(budgetB.Period!.Id);

            var tariffA = await administration.Service.CreateParkingTariffForProductAsync(
                admin.Id, productA.Id, start.AddHours(-1), end.AddHours(1), 1m, ParkingTariffUnit.Hour, ct);
            var tariffB = await administration.Service.CreateParkingTariffForProductAsync(
                admin.Id, productB.Id, start.AddHours(-1), end.AddHours(1), 2m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, tariffA.Outcome);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, tariffB.Outcome);
            tariffIds.Add(tariffA.Tariff!.Id);
            tariffIds.Add(tariffB.Tariff!.Id);

            var periodsA = await administration.Service.GetBudgetPeriodsForProductAsync(admin.Id, productA.Id, ct);
            var periodsB = await administration.Service.GetBudgetPeriodsForProductAsync(admin.Id, productB.Id, ct);
            Assert.Single(periodsA);
            Assert.Single(periodsB);
            Assert.Equal(productA.Id, periodsA[0].ProviderProductId);
            Assert.Equal(productB.Id, periodsB[0].ProviderProductId);

            var usageA = await administration.Service.GetBudgetUsageAsync(admin.Id, budgetA.Period.Id, end, ct);
            var usageB = await administration.Service.GetBudgetUsageAsync(admin.Id, budgetB.Period.Id, end, ct);
            Assert.NotNull(usageA);
            Assert.NotNull(usageB);
            Assert.Equal(60, usageA.UsedPaidDurationMinutes);
            Assert.Equal(60, usageB.UsedPaidDurationMinutes);

            var report = await administration.Service.GetCostReportAsync(
                admin.Id,
                start.AddMinutes(-1),
                end.AddMinutes(1),
                ct);

            Assert.True(report.IsComplete);
            Assert.Equal(120, report.TotalPaidDurationMinutes);
            Assert.Equal(3m, report.TotalAmount);
            Assert.Equal(2, report.Visits.Count);
            Assert.Equal(1m, report.Visits.Single(x => x.VisitId == visitA.Id).Amount);
            Assert.Equal(2m, report.Visits.Single(x => x.VisitId == visitB.Id).Amount);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderParkingActions
                .Where(x => x.VisitId == visitA.Id || x.VisitId == visitB.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.DeleteVisitSchedulerAuditEventsAsync(ct, visitA.Id, visitB.Id);
            await cleanup.Visits.Where(x => x.Id == visitA.Id || x.Id == visitB.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingBudgetPeriods.Where(x => budgetIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
            await cleanup.ParkingTariffs.Where(x => tariffIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
            await cleanup.PaidWindows
                .Where(x => x.ParkingRuleSetId == rulesA.Id || x.ParkingRuleSetId == rulesB.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingCalendarExceptions
                .Where(x => x.ParkingRuleSetId == rulesA.Id || x.ParkingRuleSetId == rulesB.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingRuleSets.Where(x => x.Id == rulesA.Id || x.Id == rulesB.Id).ExecuteDeleteAsync(ct);
            await cleanup.AdminAuditEvents
                .Where(x => x.ActorUserId == admin.Id || x.ActorUserId == visitorA.Id || x.ActorUserId == visitorB.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Users.Where(x => x.Id == admin.Id || x.Id == visitorA.Id || x.Id == visitorB.Id).ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicleA.Id || x.Id == vehicleB.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts.Where(x => x.Id == productA.Id || x.Id == productB.Id).ExecuteDeleteAsync(ct);
        }
    }

    private static ParkingRuleSet CreateRules(Guid productId)
    {
        var windows = Enumerable.Range((int)DayOfWeek.Monday, 6)
            .Select(day => new PaidWindow((DayOfWeek)day, new TimeOnly(9, 0), new TimeOnly(20, 0)))
            .ToArray();
        var rules = new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            null,
            TimeSpan.FromHours(4),
            windows,
            publicHolidaysAreFree: true,
            continuation: ProviderCoverageContinuation.StartNewAction);
        rules.AssignProviderProduct(productId);
        return rules;
    }

    private static Visit CompletedVisit(
        Guid userId,
        Guid vehicleId,
        ParkingProviderProduct product,
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
            new EffectiveParkingPolicySnapshot(null, null, true, false),
            product.Id,
            product.ProviderProductId,
            product.Location);
        visit.Activate();
        visit.BeginStopping();
        visit.Complete(end);
        return visit;
    }

    private static ProviderParkingAction CompletedProviderAction(
        Guid visitId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var action = new ProviderParkingAction(Guid.NewGuid(), visitId, start, end);
        action.MarkStarting();
        action.MarkActive($"finance-action-{Guid.NewGuid():N}", start);
        action.MarkCompleted(end);
        return action;
    }

    private async Task ResetStateAsync(CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        await context.VisitSchedulerWork.ExecuteDeleteAsync(ct);
        await context.VisitEndTimeChanges.ExecuteDeleteAsync(ct);
        await context.ProviderOperations.ExecuteDeleteAsync(ct);
        await context.ProviderParkingActions.ExecuteDeleteAsync(ct);
        await context.DeleteVisitSchedulerAuditEventsAsync(ct);
        await context.Visits.ExecuteDeleteAsync(ct);
        await context.ParkingBudgetWarningStates.ExecuteDeleteAsync(ct);
        await context.ParkingBudgetPeriods.ExecuteDeleteAsync(ct);
        await context.ParkingTariffs.ExecuteDeleteAsync(ct);
        await context.PaidWindows.ExecuteDeleteAsync(ct);
        await context.ParkingCalendarExceptions.ExecuteDeleteAsync(ct);
        await context.ParkingRuleSets.ExecuteDeleteAsync(ct);
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
