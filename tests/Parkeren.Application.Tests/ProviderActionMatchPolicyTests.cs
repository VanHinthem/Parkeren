using Parkeren.Application.ParkingProvider;
using Xunit;

namespace Parkeren.Application.Tests;

public sealed class ProviderActionMatchPolicyTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-02T12:00:00+00:00");
    private static readonly DateTimeOffset End = Start.AddHours(1);

    [Fact]
    public void Timestamp_tolerance_is_five_second_engineering_margin()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ProviderActionMatchPolicy.TimestampTolerance);
        Assert.True(ProviderActionMatchPolicy.TimestampsMatch(Start, Start.AddSeconds(5)));
        Assert.False(ProviderActionMatchPolicy.TimestampsMatch(Start, Start.AddSeconds(5).AddTicks(1)));
    }

    [Fact]
    public void Known_action_id_never_falls_back_to_semantic_candidate()
    {
        var semanticMatch = Action("other-id");
        var result = ProviderActionMatchPolicy.FindUniqueMatch(
            [semanticMatch],
            Criteria(providerActionId: "known-id"));

        Assert.Null(result);
    }

    [Fact]
    public void Product_context_is_part_of_matching()
    {
        var result = ProviderActionMatchPolicy.FindUniqueMatch(
            [Action("a", productId: "other-product")],
            Criteria(providerProductId: "product-1"));

        Assert.Null(result);
    }

    [Fact]
    public void License_plate_is_normalized_before_matching()
    {
        var action = Action("a", licensePlate: "AB-12-CD");
        var result = ProviderActionMatchPolicy.FindUniqueMatch(
            [action],
            Criteria(licensePlate: "ab12cd"));

        Assert.Same(action, result);
    }

    [Fact]
    public void Status_must_be_semantically_allowed()
    {
        var scheduled = Action("a", status: "scheduled");

        Assert.Null(ProviderActionMatchPolicy.FindUniqueMatch(
            [scheduled],
            Criteria(allowedStatuses: ["active"])));
        Assert.Same(scheduled, ProviderActionMatchPolicy.FindUniqueMatch(
            [scheduled],
            Criteria(allowedStatuses: ["active", "scheduled"])));
    }

    [Fact]
    public void Start_and_end_use_the_same_timestamp_tolerance()
    {
        var withinTolerance = Action(
            "a",
            start: Start.AddSeconds(5),
            end: End.AddSeconds(-5));
        var outsideEndTolerance = Action(
            "b",
            start: Start.AddSeconds(5),
            end: End.AddSeconds(-6));

        Assert.Same(withinTolerance, ProviderActionMatchPolicy.FindUniqueMatch(
            [withinTolerance], Criteria()));
        Assert.Null(ProviderActionMatchPolicy.FindUniqueMatch(
            [outsideEndTolerance], Criteria()));
    }

    [Fact]
    public void Fallback_without_action_id_requires_one_unique_candidate()
    {
        var first = Action("a");
        var second = Action("b", start: Start.AddSeconds(1), end: End.AddSeconds(1));

        var result = ProviderActionMatchPolicy.FindUniqueMatch(
            [first, second],
            Criteria(providerActionId: null));

        Assert.Null(result);
    }

    [Fact]
    public void Location_label_difference_is_not_an_identity_mismatch()
    {
        var action = Action("a", location: "OSS Zone J");
        var result = ProviderActionMatchPolicy.FindUniqueMatch(
            [action],
            Criteria());

        Assert.Same(action, result);
    }

    private static ProviderActionMatchCriteria Criteria(
        string? providerActionId = null,
        string? providerProductId = "product-1",
        string licensePlate = "AB12CD",
        IReadOnlyCollection<string>? allowedStatuses = null) =>
        new(
            providerActionId,
            providerProductId,
            licensePlate,
            allowedStatuses ?? ["active"],
            Start,
            End);

    private static ProviderParkingAction Action(
        string providerActionId,
        string licensePlate = "AB12CD",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string location = "OSS_J",
        string status = "active",
        string? productId = "product-1") =>
        new(
            providerActionId,
            licensePlate,
            start ?? Start,
            end ?? End,
            location,
            status,
            productId);
}
