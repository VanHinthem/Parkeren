using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Parkeren.Application.Administration;
using Parkeren.Domain.ParkingProvider;
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
    public async Task Database_rejects_overlapping_budget_periods_but_allows_adjacent_periods()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = new DateTimeOffset(2120, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var product = new ParkingProviderProduct(Guid.NewGuid(), $"BUDGET-A-{suffix}", "Budget product A", "TEST", "Test", $"BUDGET-A-{suffix}", start);
        var otherProduct = new ParkingProviderProduct(Guid.NewGuid(), $"BUDGET-B-{suffix}", "Budget product B", "TEST", "Test", $"BUDGET-B-{suffix}", start);
        var first = new ParkingBudgetPeriod(Guid.NewGuid(), start, start.AddMonths(1), TimeSpan.FromHours(2));
        var adjacent = new ParkingBudgetPeriod(Guid.NewGuid(), start.AddMonths(1), start.AddMonths(2), TimeSpan.FromHours(2));
        var overlapping = new ParkingBudgetPeriod(Guid.NewGuid(), start.AddDays(1), start.AddMonths(1).AddDays(1), TimeSpan.FromHours(2));
        var otherProductOverlap = new ParkingBudgetPeriod(Guid.NewGuid(), start.AddDays(1), start.AddMonths(1).AddDays(1), TimeSpan.FromHours(2));
        first.AssignProviderProduct(product.Id);
        adjacent.AssignProviderProduct(product.Id);
        overlapping.AssignProviderProduct(product.Id);
        otherProductOverlap.AssignProviderProduct(otherProduct.Id);

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.AddRange(product, otherProduct);
                seed.ParkingBudgetPeriods.AddRange(first, adjacent);
                await seed.SaveChangesAsync(ct);
            }

            await using (var otherScope = fixture.CreateDbContext())
            {
                otherScope.ParkingBudgetPeriods.Add(otherProductOverlap);
                await otherScope.SaveChangesAsync(ct);
            }

            await using var conflict = fixture.CreateDbContext();
            conflict.ParkingBudgetPeriods.Add(overlapping);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => conflict.SaveChangesAsync(ct));
            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.ExclusionViolation, postgresException.SqlState);
            Assert.Equal("ex_parking_budget_periods_productperiod", postgresException.ConstraintName);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingBudgetPeriods
                .Where(x => x.Id == first.Id || x.Id == adjacent.Id || x.Id == overlapping.Id || x.Id == otherProductOverlap.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts
                .Where(x => x.Id == product.Id || x.Id == otherProduct.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Database_rejects_overlapping_tariffs_but_allows_adjacent_tariffs()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = new DateTimeOffset(2120, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var product = new ParkingProviderProduct(Guid.NewGuid(), $"TARIFF-A-{suffix}", "Tariff product A", "TEST", "Test", $"TARIFF-A-{suffix}", start);
        var otherProduct = new ParkingProviderProduct(Guid.NewGuid(), $"TARIFF-B-{suffix}", "Tariff product B", "TEST", "Test", $"TARIFF-B-{suffix}", start);
        var first = new ParkingTariff(Guid.NewGuid(), start, start.AddMonths(1), 1m);
        var adjacent = new ParkingTariff(Guid.NewGuid(), start.AddMonths(1), start.AddMonths(2), 1m);
        var overlapping = new ParkingTariff(Guid.NewGuid(), start.AddDays(1), null, 1m);
        var otherProductOverlap = new ParkingTariff(Guid.NewGuid(), start.AddDays(1), null, 1m);
        first.AssignProviderProduct(product.Id);
        adjacent.AssignProviderProduct(product.Id);
        overlapping.AssignProviderProduct(product.Id);
        otherProductOverlap.AssignProviderProduct(otherProduct.Id);

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.AddRange(product, otherProduct);
                seed.ParkingTariffs.AddRange(first, adjacent);
                await seed.SaveChangesAsync(ct);
            }

            await using (var otherScope = fixture.CreateDbContext())
            {
                otherScope.ParkingTariffs.Add(otherProductOverlap);
                await otherScope.SaveChangesAsync(ct);
            }

            await using var conflict = fixture.CreateDbContext();
            conflict.ParkingTariffs.Add(overlapping);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => conflict.SaveChangesAsync(ct));
            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.ExclusionViolation, postgresException.SqlState);
            Assert.Equal("ex_parking_tariffs_productperiod", postgresException.ConstraintName);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ParkingTariffs
                .Where(x => x.Id == first.Id || x.Id == adjacent.Id || x.Id == overlapping.Id || x.Id == otherProductOverlap.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.ParkingProviderProducts
                .Where(x => x.Id == product.Id || x.Id == otherProduct.Id)
                .ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task Database_rejects_invalid_validity_intervals_for_all_period_tables()
    {
        var ct = TestContext.Current.CancellationToken;
        var instant = new DateTimeOffset(2120, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var budget = fixture.CreateDbContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => budget.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO parking_budget_periods (\"Id\", \"ValidFrom\", \"ValidUntil\", \"MaximumPaidDuration\") VALUES ({Guid.NewGuid()}, {instant}, {instant}, {TimeSpan.FromHours(1)})",
                ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_parking_budget_periods_validinterval", exception.ConstraintName);
        }

        await using (var tariff = fixture.CreateDbContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => tariff.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO parking_tariffs (\"Id\", \"ValidFrom\", \"ValidUntil\", \"Rate\", \"Unit\") VALUES ({Guid.NewGuid()}, {instant}, {instant}, {1m}, {(int)ParkingTariffUnit.Hour})",
                ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_parking_tariffs_validinterval", exception.ConstraintName);
        }

        await using (var ruleSet = fixture.CreateDbContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ruleSet.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO parking_rule_sets (\"Id\", \"ValidFrom\", \"ValidUntil\", \"MaxProviderActionDuration\", \"Continuation\", \"PublicHolidaysAreFree\") VALUES ({Guid.NewGuid()}, {instant}, {instant}, {TimeSpan.FromHours(1)}, {ProviderCoverageContinuation.StartNewAction.ToString()}, {false})",
                ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_parking_rule_sets_validinterval", exception.ConstraintName);
        }
    }

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
    public async Task Budget_usage_includes_provider_action_overlap_before_visit_start()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var boundary = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        var actionStart = boundary.AddMinutes(-1);
        var visitStart = boundary.AddMinutes(1);
        CompletedVisitSeed? visitSeed = null;
        var budgetIds = new List<Guid>();

        try
        {
            visitSeed = await CreateCompletedVisitAsync(
                visitStart,
                boundary.AddMinutes(15),
                ct,
                actionStartAt: actionStart,
                actionEndAt: boundary.AddMinutes(10));
            await using var administration = CreateAdministration();
            var previous = await administration.Service.CreateBudgetPeriodAsync(
                admin.Id, boundary.AddHours(-1), boundary, 10 * 60, ct);
            var current = await administration.Service.CreateBudgetPeriodAsync(
                admin.Id, boundary, boundary.AddHours(1), 10 * 60, ct);
            Assert.Equal(AdminBudgetPeriodCreateOutcome.Created, previous.Outcome);
            Assert.Equal(AdminBudgetPeriodCreateOutcome.Created, current.Outcome);
            budgetIds.Add(previous.Period!.Id);
            budgetIds.Add(current.Period!.Id);

            var usage = await administration.Service.GetBudgetUsageAsync(
                admin.Id, previous.Period.Id, boundary.AddMinutes(-1), ct);

            Assert.NotNull(usage);
            Assert.True(usage.IsComplete);
            Assert.Equal(1, usage.UsedPaidDurationMinutes);
        }
        finally
        {
            if (visitSeed is not null)
                await CleanupVisitorAsync(visitSeed.UserId, visitSeed.VehicleId, ct);
            await CleanupAsync(admin.Id, budgetIds, [], ct);
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
    public async Task Cost_report_uses_provider_action_interval_instead_of_visit_interval()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var localOffset = TimeSpan.FromHours(2);
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, localOffset).ToUniversalTime();
        var end = start.AddHours(2);
        var actionStart = start.AddMinutes(15);
        var actionEnd = start.AddMinutes(45);
        var tariffIds = new List<Guid>();
        CompletedVisitSeed? visitSeed = null;

        try
        {
            visitSeed = await CreateCompletedVisitAsync(start, end, ct, actionStart, actionEnd);

            var schedulerWork = new VisitSchedulerWork(
                Guid.NewGuid(), visitSeed.VisitId, VisitSchedulerWorkType.StopVisit,
                end, VisitEndReason.DesiredEndReached);
            var pendingEndTimeChange = new VisitEndTimeChange(
                Guid.NewGuid(), Guid.NewGuid(), visitSeed.VisitId, admin.Id,
                end, end.AddMinutes(15), start.AddMinutes(-3));
            var rejectedEndTimeChange = new VisitEndTimeChange(
                Guid.NewGuid(), Guid.NewGuid(), visitSeed.VisitId, admin.Id,
                end, end.AddMinutes(-15), start.AddMinutes(-2));
            rejectedEndTimeChange.MarkRejected();
            var appliedEndTimeChange = new VisitEndTimeChange(
                Guid.NewGuid(), Guid.NewGuid(), visitSeed.VisitId, admin.Id,
                end, end.AddMinutes(30), start.AddMinutes(-1));
            appliedEndTimeChange.MarkApplied();
            await using (var seedWork = fixture.CreateDbContext())
            {
                seedWork.VisitSchedulerWork.Add(schedulerWork);
                seedWork.VisitEndTimeChanges.AddRange(
                    pendingEndTimeChange, rejectedEndTimeChange, appliedEndTimeChange);
                await seedWork.SaveChangesAsync(ct);
            }

            await using var administration = CreateAdministration();
            var tariff = await administration.Service.CreateParkingTariffAsync(
                admin.Id, start.AddHours(-1), end.AddHours(1), 1m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, tariff.Outcome);
            tariffIds.Add(tariff.Tariff!.Id);

            var report = await administration.Service.GetCostReportAsync(
                admin.Id, start.AddMinutes(-1), end.AddMinutes(1), ct);

            Assert.True(report.IsComplete);
            Assert.Equal(30, report.TotalPaidDurationMinutes);
            Assert.Equal(0.5m, report.TotalAmount);
            var visit = Assert.Single(report.Visits);
            Assert.Equal(30, visit.PaidDurationMinutes);
            Assert.Equal(0.5m, visit.Amount);

            var visitSummaries = await administration.Service.GetVisitsAsync(
                admin.Id, visitSeed.UserId, null, null, null, VisitStatus.Completed, end, ct);
            Assert.Equal(30, Assert.Single(visitSummaries).PaidDurationMinutes);
            var visitDetail = await administration.Service.GetVisitDetailAsync(
                admin.Id, visitSeed.VisitId, end, ct);
            Assert.NotNull(visitDetail);
            Assert.Equal(30, visitDetail.Visit.PaidDurationMinutes);
            Assert.Contains(visitDetail.TimelineEvents, x => x.SourceType == "visit");
            Assert.Contains(visitDetail.TimelineEvents, x => x.SourceType == "provider_action");
            var projectedWork = Assert.Single(visitDetail.SchedulerWork);
            Assert.Equal(schedulerWork.Id, projectedWork.Id);
            Assert.Contains(visitDetail.TimelineEvents, x =>
                x.SourceType == "scheduler_work" && x.SourceId == projectedWork.Id);
            var pendingEvent = Assert.Single(visitDetail.TimelineEvents, x =>
                x.SourceId == pendingEndTimeChange.Id);
            var rejectedEvent = Assert.Single(visitDetail.TimelineEvents, x =>
                x.SourceId == rejectedEndTimeChange.Id);
            var appliedEvent = Assert.Single(visitDetail.TimelineEvents, x =>
                x.SourceId == appliedEndTimeChange.Id);
            Assert.Equal("visit.desired_end_change_requested", pendingEvent.EventType);
            Assert.Equal("visit.desired_end_change_rejected", rejectedEvent.EventType);
            Assert.Equal("visit.desired_end_change_applied", appliedEvent.EventType);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.Service.GetVisitDetailAsync(visitSeed.UserId, visitSeed.VisitId, end, ct));
        }
        finally
        {
            if (visitSeed is not null)
                await CleanupVisitorAsync(visitSeed.UserId, visitSeed.VehicleId, ct);
            await CleanupAsync(admin.Id, [], tariffIds, ct);
        }
    }

    [Fact]
    public async Task Cost_report_uses_reconciled_provider_cost_when_action_is_fully_in_period()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime();
        var end = start.AddHours(1);
        var tariffIds = new List<Guid>();
        CompletedVisitSeed? visitSeed = null;

        try
        {
            visitSeed = await CreateCompletedVisitAsync(start, end, ct, providerHistoryCost: 0.42m);
            await using var administration = CreateAdministration();
            var tariff = await administration.Service.CreateParkingTariffAsync(
                admin.Id, start.AddHours(-1), end.AddHours(1), 1m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, tariff.Outcome);
            tariffIds.Add(tariff.Tariff!.Id);

            var report = await administration.Service.GetCostReportAsync(
                admin.Id, start.AddMinutes(-1), end.AddMinutes(1), ct);

            Assert.True(report.IsComplete);
            Assert.Equal(60, report.TotalPaidDurationMinutes);
            Assert.Equal(0.42m, report.TotalAmount);
            Assert.Equal(0.42m, Assert.Single(report.Visits).Amount);
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

    [Fact]
    public async Task Cost_report_is_incomplete_when_provider_history_is_incomplete()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime();
        var end = start.AddHours(1);
        var tariffIds = new List<Guid>();
        CompletedVisitSeed? visitSeed = null;

        try
        {
            visitSeed = await CreateCompletedVisitAsync(start, end, ct, historyIncomplete: true);
            await using var administration = CreateAdministration();
            var tariff = await administration.Service.CreateParkingTariffAsync(
                admin.Id, start.AddHours(-1), end.AddHours(1), 1m, ParkingTariffUnit.Hour, ct);
            Assert.Equal(AdminParkingTariffCreateOutcome.Created, tariff.Outcome);
            tariffIds.Add(tariff.Tariff!.Id);

            var report = await administration.Service.GetCostReportAsync(
                admin.Id, start.AddMinutes(-1), end.AddMinutes(1), ct);

            Assert.False(report.IsComplete);
            Assert.Null(report.TotalAmount);
            var visit = Assert.Single(report.Visits);
            Assert.Equal(1m, visit.Amount);
            Assert.False(visit.IsComplete);
            Assert.Equal("Provider action history is incomplete.", visit.Error);
        }
        finally
        {
            if (visitSeed is not null)
                await CleanupVisitorAsync(visitSeed.UserId, visitSeed.VehicleId, ct);
            await CleanupAsync(admin.Id, [], tariffIds, ct);
        }
    }

    [Fact]
    public async Task Admin_budget_includes_imported_provider_action_without_visit()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = await CreateAdminAsync(ct);
        await ResetCalculationStateAsync(ct);
        var suffix = Guid.NewGuid().ToString("N");
        var product = new ParkingProviderProduct(Guid.NewGuid(), $"IMPORT-{suffix}",
            "Imported budget test", "TEST", "Test", $"LOC-{suffix}",
            new DateTimeOffset(2026, 9, 28, 7, 0, 0, TimeSpan.Zero));
        var periodStart = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var budget = new ParkingBudgetPeriod(Guid.NewGuid(), periodStart,
            periodStart.AddDays(1), TimeSpan.FromHours(10));
        budget.AssignProviderProduct(product.Id);
        var plate = $"IB{suffix[..6].ToUpperInvariant()}";
        var vehicle = Vehicle.FromProviderHistory(Guid.NewGuid(), plate);
        var start = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var action = ProviderParkingAction.ImportCompleted(Guid.NewGuid(),
            $"import-budget-{suffix}", product.ProviderProductId, product.Location,
            vehicle.Id, ProviderActionAssignment.Unassigned, start,
            start.AddHours(1), 0.25m, "COMPLETED", start.AddHours(2));

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ParkingProviderProducts.Add(product);
                seed.ParkingBudgetPeriods.Add(budget);
                seed.Vehicles.Add(vehicle);
                seed.ProviderParkingActions.Add(action);
                await seed.SaveChangesAsync(ct);
            }

            await using var administration = CreateAdministration();
            var usage = await administration.Service.GetBudgetUsageAsync(
                admin.Id, budget.Id, start.AddHours(3), ct);

            Assert.NotNull(usage);
            Assert.True(usage.IsComplete);
            Assert.Equal(60, usage.UsedPaidDurationMinutes);
            Assert.Equal(540, usage.RemainingPaidDurationMinutes);
        }
        finally
        {
            await using (var cleanup = fixture.CreateDbContext())
            {
                await cleanup.ProviderParkingActions.Where(x => x.Id == action.Id).ExecuteDeleteAsync(ct);
                await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
                await cleanup.ParkingBudgetPeriods.Where(x => x.Id == budget.Id).ExecuteDeleteAsync(ct);
                await cleanup.ParkingProviderProducts.Where(x => x.Id == product.Id).ExecuteDeleteAsync(ct);
            }
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
        CancellationToken ct,
        DateTimeOffset? actionStartAt = null,
        DateTimeOffset? actionEndAt = null,
        bool historyIncomplete = false,
        decimal? providerHistoryCost = null)
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
        var action = new ProviderParkingAction(Guid.NewGuid(), visit.Id, start, end);
        action.MarkStarting();
        action.MarkActive($"provider-{Guid.NewGuid():N}", actionStartAt ?? start);
        action.MarkCompleted(actionEndAt ?? end);
        if (historyIncomplete)
        {
            action.ScheduleHistoryReconciliation();
            action.MarkHistoryIncomplete();
        }
        else if (providerHistoryCost is decimal cost)
        {
            action.ScheduleHistoryReconciliation();
            action.ApplyProviderHistory(actionStartAt ?? start, actionEndAt ?? end, cost);
        }

        await using var context = fixture.CreateDbContext();
        context.Users.Add(visitor);
        context.Vehicles.Add(vehicle);
        context.Visits.Add(visit);
        context.ProviderParkingActions.Add(action);
        await context.SaveChangesAsync(ct);
        return new CompletedVisitSeed(visitor.Id, vehicle.Id, visit.Id);
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
        await context.DeleteVisitSchedulerAuditEventsAsync(ct);
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

    private sealed record CompletedVisitSeed(Guid UserId, Guid VehicleId, Guid VisitId);

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
