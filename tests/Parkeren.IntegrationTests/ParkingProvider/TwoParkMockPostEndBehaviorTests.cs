using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Parkeren.Application.ParkingProvider;
using Parkeren.Infrastructure.ParkingProvider;

namespace Parkeren.IntegrationTests.ParkingProvider;

public sealed class TwoParkMockPostEndBehaviorTests
{
    [Fact]
    public async Task Post_end_behavior_is_only_applied_when_explicitly_configured()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var end = now.AddHours(1);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("POSTEND1", now, end, "Oss"),
            cancellationToken);

        (await http.PostAsJsonAsync("api/test/clock/advance", new { Milliseconds = 2 * 60 * 60 * 1000 }, cancellationToken)).EnsureSuccessStatusCode();

        var defaultReadback = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("active", defaultReadback.Status, ignoreCase: true);

        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "completed" }, cancellationToken)).EnsureSuccessStatusCode();
        var completed = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("completed", completed.Status, ignoreCase: true);

        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "keep-active" }, cancellationToken)).EnsureSuccessStatusCode();
        var active = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("active", active.Status, ignoreCase: true);

        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "hide" }, cancellationToken)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
    }

    [Fact]
    public async Task Explicit_stop_wins_over_post_end_hide_mode()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("POSTEND2", now, now.AddHours(1), "Oss"),
            cancellationToken);
        await provider.StopActionAsync(created.ProviderActionId, cancellationToken);

        (await http.PostAsJsonAsync("api/test/clock/advance", new { Milliseconds = 2 * 60 * 60 * 1000 }, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/post-end-behavior", new { Behavior = "hide" }, cancellationToken)).EnsureSuccessStatusCode();

        var stopped = Assert.Single(await provider.GetActionsAsync(cancellationToken),
            action => action.ProviderActionId == created.ProviderActionId);
        Assert.Equal("stopped", stopped.Status, ignoreCase: true);
    }

    [Fact]
    public async Task Action_history_pages_include_records_on_later_pages()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var product = await provider.GetProductAsync(cancellationToken);
        var actionIds = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var action = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    $"HISTORY{index}", now.AddMinutes(-10 + index), now.AddHours(1), product.Location, product.Id),
                cancellationToken);
            actionIds.Add(action.ProviderActionId);
            await provider.StopActionAsync(action.ProviderActionId, cancellationToken);
        }

        var firstPage = await provider.GetActionHistoryPageAsync(product.Id, 0, 2, cancellationToken);
        var secondPage = await provider.GetActionHistoryPageAsync(product.Id, 1, 2, cancellationToken);

        Assert.Collection(firstPage.Records, _ => { }, _ => { });
        Assert.True(firstPage.HasMore);
        var secondPageRecord = Assert.Single(secondPage.Records);
        Assert.False(secondPage.HasMore);
        Assert.Equal(3, secondPage.TotalCount);
        Assert.Equal("COMPLETED", secondPageRecord.Status);
        Assert.Equal(0.01m, secondPageRecord.ProviderCostAmount);
        Assert.Equal("EUR", secondPageRecord.Currency);
        Assert.Equal(3, firstPage.Records.Concat(secondPage.Records)
            .Select(x => x.ProviderActionId)
            .Intersect(actionIds)
            .Count());
    }

    [Fact]
    public async Task Stopped_action_history_reports_realized_interval_without_changing_planned_readback()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var start = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var plannedEnd = start.AddHours(1);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = start }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var product = await provider.GetProductAsync(cancellationToken);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("REALIZED1", start, plannedEnd, product.Location, product.Id), cancellationToken);
        var stoppedAt = start.AddMinutes(12);
        (await http.PostAsJsonAsync("api/test/clock/advance", new { Milliseconds = 12 * 60 * 1000 }, cancellationToken)).EnsureSuccessStatusCode();
        await provider.StopActionAsync(created.ProviderActionId, cancellationToken);

        var readback = Assert.Single(await provider.GetActionsAsync(cancellationToken));
        Assert.Equal(start, readback.Start);
        Assert.Equal(plannedEnd, readback.End);
        var history = Assert.Single((await provider.GetActionHistoryPageAsync(product.Id, 0, 10, cancellationToken)).Records);
        Assert.Equal(start, history.ActualStartAt);
        Assert.Equal(stoppedAt, history.ActualEndAt);
    }

    [Fact]
    public async Task Cancelling_scheduled_action_records_zero_length_history_at_cancellation_time()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var scheduledStart = now.AddHours(1);
        var plannedEnd = scheduledStart.AddHours(1);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var product = await provider.GetProductAsync(cancellationToken);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest("CANCEL1", scheduledStart, plannedEnd, product.Location, product.Id), cancellationToken);
        await provider.StopActionAsync(created.ProviderActionId, cancellationToken);

        var readback = Assert.Single(await provider.GetActionsAsync(cancellationToken));
        Assert.Equal("stopped", readback.Status, ignoreCase: true);
        Assert.Equal(scheduledStart, readback.Start);
        Assert.Equal(plannedEnd, readback.End);
        var history = Assert.Single((await provider.GetActionHistoryPageAsync(product.Id, 0, 10, cancellationToken)).Records);
        Assert.Equal(now, history.ActualStartAt);
        Assert.Equal(now, history.ActualEndAt);
    }

    [Fact]
    public async Task Action_history_visibility_can_be_delayed_and_records_can_be_missing()
    {
        await using var factory = new WebApplicationFactory<Parkeren.TwoParkMock.Program>();
        using var http = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        (await http.PostAsync("api/test/reset", null, cancellationToken)).EnsureSuccessStatusCode();
        (await http.PostAsJsonAsync("api/test/clock/set", new { UtcNow = now }, cancellationToken)).EnsureSuccessStatusCode();

        var provider = new TwoParkMockProvider(http);
        var product = await provider.GetProductAsync(cancellationToken);
        var delayedAction = await provider.StartActionAsync(
            new ProviderParkingActionRequest("HISTORY-DELAY", now.AddMinutes(-5), now.AddHours(1), product.Location, product.Id),
            cancellationToken);
        (await http.PostAsJsonAsync(
            "api/test/action-history",
            new { VisibilityDelayMilliseconds = 60_000 },
            cancellationToken)).EnsureSuccessStatusCode();
        await provider.StopActionAsync(delayedAction.ProviderActionId, cancellationToken);

        var notYetVisible = await provider.GetActionHistoryPageAsync(product.Id, 0, 1, cancellationToken);
        Assert.Empty(notYetVisible.Records);
        Assert.Equal(0, notYetVisible.TotalCount);

        (await http.PostAsJsonAsync(
            "api/test/clock/advance",
            new { Milliseconds = 60_000 },
            cancellationToken)).EnsureSuccessStatusCode();

        var visibleOnRetry = await provider.GetActionHistoryPageAsync(product.Id, 0, 1, cancellationToken);
        Assert.Equal(delayedAction.ProviderActionId, Assert.Single(visibleOnRetry.Records).ProviderActionId);

        var missingAction = await provider.StartActionAsync(
            new ProviderParkingActionRequest("HISTORY-MISSING", now.AddMinutes(-4), now.AddHours(1), product.Location, product.Id),
            cancellationToken);
        (await http.PostAsJsonAsync(
            "api/test/action-history",
            new { MissingActionIds = new[] { missingAction.ProviderActionId } },
            cancellationToken)).EnsureSuccessStatusCode();
        await provider.StopActionAsync(missingAction.ProviderActionId, cancellationToken);

        var allRecords = new List<ProviderActionHistoryRecord>();
        var pageNumber = 0;
        ProviderActionHistoryPage page;
        do
        {
            page = await provider.GetActionHistoryPageAsync(product.Id, pageNumber++, 1, cancellationToken);
            allRecords.AddRange(page.Records);
        }
        while (page.HasMore);

        Assert.Contains(allRecords, x => x.ProviderActionId == delayedAction.ProviderActionId);
        Assert.DoesNotContain(allRecords, x => x.ProviderActionId == missingAction.ProviderActionId);
    }
}
