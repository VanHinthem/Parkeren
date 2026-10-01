using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Xunit;

namespace Parkeren.Domain.Tests;

public sealed class CandidateVisitAssessorTests
{
    [Fact]
    public void Candidate_visit_is_assessed_across_versioned_rules_tariffs_and_budget()
    {
        var boundary = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        var windows = new[] { new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)) };
        var rules = new[]
        {
            new ParkingRuleSet(Guid.NewGuid(), boundary.AddDays(-1), boundary, TimeSpan.FromHours(4), windows),
            new ParkingRuleSet(Guid.NewGuid(), boundary, null, TimeSpan.FromHours(4), windows)
        };
        var tariffs = new[]
        {
            new ParkingTariff(Guid.NewGuid(), boundary.AddDays(-1), boundary, 1m),
            new ParkingTariff(Guid.NewGuid(), boundary, null, 2m)
        };
        var budgets = new[]
        {
            new ParkingBudgetPeriod(Guid.NewGuid(), boundary.AddYears(-1), boundary.AddYears(1), TimeSpan.FromHours(1500))
        };
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(10), true);

        var result = CandidateVisitAssessor.Assess(
            boundary.AddHours(-1), boundary.AddHours(1), rules, policy, TimeSpan.Zero, tariffs, budgets);

        Assert.True(result.IsAllowed);
        Assert.Equal(TimeSpan.FromHours(2), result.Usage.PaidDuration);
        Assert.Equal(3m, result.TotalCost);
        Assert.Equal(TimeSpan.FromHours(2), result.BudgetAllocations.Aggregate(TimeSpan.Zero, (x, y) => x + y.PaidDuration));
    }
}
