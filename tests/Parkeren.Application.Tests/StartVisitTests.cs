using Parkeren.Application.Visits;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Users;
using Xunit;

namespace Parkeren.Application.Tests;
public sealed class StartVisitTests
{
    [Fact]
    public void Prepare_allows_longer_visit_when_a_second_provider_action_is_permitted()
    {
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        var ownerId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), ownerId, ownerId, Guid.NewGuid(),
            start, start.AddHours(5));
        var context = new StartVisitContext(new(ownerId, UserRole.Visitor, true),
            new(ownerId, true), new(command.VehicleId, true, true));
        var rules = new[]
        {
            new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1), null, TimeSpan.FromHours(4),
                [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0))])
        };
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true);

        var prepared = new StartVisitPreparer().Prepare(
            command, context, policy, rules, start.AddMinutes(1));
        Assert.True(prepared.RequiresProviderCoverageNow);
        Assert.Equal(start.AddHours(4), ProviderActionStartPlanner.PlanEnd(start, command.DesiredEndAt!.Value, rules));

        var noVisitExtension = policy with { AllowVisitExtension = false };
        var preparedWithoutVisitExtension = new StartVisitPreparer().Prepare(
            command, context, noVisitExtension, rules, start.AddMinutes(1));
        Assert.True(preparedWithoutVisitExtension.RequiresProviderCoverageNow);
    }

    [Fact]
    public void Initial_action_stops_at_paid_window_end_even_when_visit_continues_overnight()
    {
        // 19:00 local on Monday until 10:00 local on Tuesday.
        var start = new DateTimeOffset(2026, 9, 28, 17, 0, 0, TimeSpan.Zero);
        var desiredEnd = new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
        var rules = new[]
        {
            new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1), null, TimeSpan.FromHours(4),
                [new PaidWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(20, 0)),
                 new PaidWindow(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(20, 0))])
        };
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), start, desiredEnd);
        var context = new StartVisitContext(new(userId, UserRole.Visitor, true),
            new(userId, true), new(command.VehicleId, true, true));
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(16), true);

        Assert.Equal(start.AddHours(1), ProviderActionStartPlanner.PlanEnd(start, desiredEnd, rules));
        Assert.True(new StartVisitPreparer().Prepare(command, context, policy, rules, start.AddMinutes(1))
            .RequiresProviderCoverageNow);
        Assert.True(new StartVisitPreparer().Prepare(
            command, context, policy with { AllowVisitExtension = false }, rules, start.AddMinutes(1))
            .RequiresProviderCoverageNow);

        var freeOnlyEnd = start.AddHours(2);
        var withoutFurtherPaidTime = command with { DesiredEndAt = freeOnlyEnd };
        new StartVisitPreparer().Prepare(
            withoutFurtherPaidTime, context, policy with { AllowVisitExtension = false }, rules, start.AddMinutes(1));
    }

    [Fact]
    public void Prepare_rejects_open_ended_visit_when_policy_disallows_it()
    {
        var start = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), start, null);
        var context = new StartVisitContext(new(userId, UserRole.Visitor, true),
            new(userId, true), new(command.VehicleId, true, true));
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(8), true, false);

        Assert.Throws<InvalidOperationException>(() => new StartVisitPreparer().Prepare(
            command, context, policy, PaidRules(start), start.AddMinutes(1)));
    }

    [Fact]
    public void Prepare_allows_open_ended_visit_when_policy_allows_it()
    {
        var start = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), start, null);
        var context = new StartVisitContext(new(userId, UserRole.Visitor, true),
            new(userId, true), new(command.VehicleId, true, true));
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(8), false, true);

        var prepared = new StartVisitPreparer().Prepare(
            command, context, policy, PaidRules(start), start.AddMinutes(1));

        Assert.Null(prepared.Visit.DesiredEndAt);
    }

    [Fact]
    public void Prepare_captures_policy_and_keeps_visit_starting_until_capacity_and_provider_work_are_committed()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(8), TimeSpan.FromHours(12), true);
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        var context = new StartVisitContext(new(command.ActorUserId, UserRole.Visitor, true), new(command.OwnerUserId, true), new(command.VehicleId, true, true));
        var rules = PaidRules(command.StartAt);
        var result = new StartVisitPreparer().Prepare(command, context, policy, rules, command.StartAt.AddMinutes(1));
        Assert.Equal(VisitStatus.Starting, result.Visit.Status);
        Assert.True(result.RequiresProviderCoverageNow);
        Assert.Equal(command.OperationId, result.OperationId);
        Assert.Equal(policy.MaxPaidParkingDuration, result.Visit.PolicySnapshot.MaxPaidParkingDuration);
    }
    private static ParkingRuleSet[] PaidRules(DateTimeOffset start) =>
    [
        new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1), null, TimeSpan.FromHours(4),
            [new PaidWindow(TimeZoneInfo.ConvertTime(start, TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId)).DayOfWeek, TimeOnly.MinValue, new TimeOnly(23, 59, 59))])
    ];
}


public sealed class StartVisitClaimerTests
{
    [Fact]
    public async Task Claim_reuses_existing_visit_for_replayed_operation()
    {
        var policy = new EffectiveParkingPolicy(TimeSpan.FromHours(4), TimeSpan.FromHours(8), true);
        var userId = Guid.NewGuid();
        var command = new StartVisitCommand(Guid.NewGuid(), userId, userId, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        var context = new StartVisitContext(new(command.ActorUserId, UserRole.Visitor, true), new(command.OwnerUserId, true), new(command.VehicleId, true, true));
        var rules = PaidRules(command.StartAt);
        var preparation = new StartVisitPreparer().Prepare(command, context, policy, rules, command.StartAt.AddMinutes(1));
        var existing = preparation.Visit;
        var claimer = new StartVisitClaimer(new ReplayCapacityClaimer(existing));

        var result = await claimer.ClaimAsync(preparation, 5, 1, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsReplay);
        Assert.Same(existing, result.Visit);
        Assert.True(result.RequiresProviderCoverageNow);
    }

    private static ParkingRuleSet[] PaidRules(DateTimeOffset start) =>
    [
        new ParkingRuleSet(Guid.NewGuid(), start.AddDays(-1), null, TimeSpan.FromHours(4),
            [new PaidWindow(TimeZoneInfo.ConvertTime(start, TimeZoneInfo.FindSystemTimeZoneById(ParkingTimeSegmenter.BusinessTimeZoneId)).DayOfWeek, TimeOnly.MinValue, new TimeOnly(23, 59, 59))])
    ];

    private sealed class ReplayCapacityClaimer(Visit existing) : IVisitCapacityClaimer
    {
        public Task<VisitCapacityClaim> TryClaimAsync(Visit visit, int maxGlobalConcurrentVisits, int maxUserConcurrentVisits, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VisitCapacityClaim(true, existing, true));
    }
}
