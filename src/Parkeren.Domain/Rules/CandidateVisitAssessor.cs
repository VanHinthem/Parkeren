using Parkeren.Domain.Policies;

namespace Parkeren.Domain.Rules;

public sealed record CandidateVisitAssessment(
    IReadOnlyList<ParkingTimeSegment> Segments,
    ParkingUsage Usage,
    ParkingPolicyValidationResult PaidDuration,
    VisitDurationValidationResult ElapsedDuration,
    IReadOnlyList<ParkingTariffCost> Costs,
    IReadOnlyList<ParkingBudgetAllocation> BudgetAllocations)
{
    public bool IsAllowed => PaidDuration.IsAllowed && ElapsedDuration.IsAllowed;
    public decimal TotalCost => Costs.Sum(x => x.Amount);
}

public static class CandidateVisitAssessor
{
    public static CandidateVisitAssessment Assess(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<ParkingRuleSet> ruleSets,
        EffectiveParkingPolicy policy,
        TimeSpan alreadyUsedPaidDuration,
        IEnumerable<ParkingTariff> tariffs,
        IEnumerable<ParkingBudgetPeriod> budgetPeriods)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(tariffs);
        ArgumentNullException.ThrowIfNull(budgetPeriods);

        var tariffArray = tariffs.ToArray();
        var budgetArray = budgetPeriods.ToArray();
        ParkingTariffResolver.ValidateNoOverlap(tariffArray);
        ParkingBudgetPeriodResolver.ValidateNoOverlap(budgetArray);

        var segments = ParkingRuleSetPeriodSegmenter.Segment(start, end, ruleSets)
            .SelectMany(x => ParkingTimeSegmenter.Segment(x.Start, x.End, x.RuleSet))
            .ToArray();

        var usage = ParkingUsageCalculator.Calculate(segments);
        var paid = ParkingPolicyValidator.ValidatePaidDuration(policy, alreadyUsedPaidDuration, usage.PaidDuration);
        var elapsed = VisitDurationPolicyValidator.Validate(policy, start, end);
        var costs = segments.Where(x => x.IsPaid)
            .SelectMany(x => ParkingTariffCostCalculator.Calculate(x, tariffArray)).ToArray();
        var budget = segments.Where(x => x.IsPaid)
            .SelectMany(x => ParkingBudgetAllocator.Allocate(x, budgetArray)).ToArray();

        return new CandidateVisitAssessment(segments, usage, paid, elapsed, costs, budget);
    }
}
