using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var actions = new ConcurrentDictionary<string, MockParkingAction>();
var remainingMinutes = 1500 * 60;
var maxConcurrentActions = 5;
var failure = new MockFailureState();
var outcome = new MockUnknownOutcomeState();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "2park-mock" }));

app.MapGet("/api/balance", async () =>
{
    if (await failure.ApplyAsync()) return Results.StatusCode(failure.StatusCode);
    return Results.Ok(new
{
    remainingPaidMinutes = remainingMinutes,
    retrievedAt = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/actions", () => Results.Ok(actions.Values.OrderBy(x => x.Start)));

app.MapPost("/api/actions", async (MockActionRequest request) =>
{
    if (await failure.ApplyAsync()) return Results.StatusCode(failure.StatusCode);
    if (request.End <= request.Start)
        return Results.BadRequest(new { error = "End must be after start." });

    if (actions.Values.Count(x => x.Status == "active") >= maxConcurrentActions)
        return Results.Conflict(new { error = "Provider capacity reached." });

    var id = Guid.NewGuid().ToString("N");
    var action = new MockParkingAction(id, request.LicensePlate, request.Start, request.End, request.Location, "active");
    actions[id] = action;
    if (await outcome.ApplyAsync()) return Results.StatusCode(outcome.StatusCode);
    return Results.Created($"/api/actions/{id}", action);
});

app.MapPut("/api/actions/{id}/end", (string id, MockExtendRequest request) =>
{
    if (!actions.TryGetValue(id, out var current))
        return Results.NotFound();

    if (request.End <= current.Start)
        return Results.BadRequest(new { error = "End must be after start." });

    var updated = current with { End = request.End };
    actions[id] = updated;
    return Results.Ok(updated);
});

app.MapPost("/api/actions/{id}/stop", (string id) =>
{
    if (!actions.TryGetValue(id, out var current))
        return Results.NotFound();

    actions[id] = current with { Status = "stopped" };
    return Results.NoContent();
});

app.MapPost("/api/test/unknown-outcome", (MockUnknownOutcomeRequest request) =>
{
    outcome.Configure(request.StatusCode, request.DelayMilliseconds, request.Count);
    return Results.NoContent();
});

app.MapPost("/api/test/capacity", (MockCapacityRequest request) =>
{
    maxConcurrentActions = Math.Max(1, request.MaxConcurrentActions);
    return Results.NoContent();
});

app.MapPost("/api/test/reset", () =>
{
    actions.Clear();
    failure.Reset();
    outcome.Reset();
    maxConcurrentActions = 5;
    return Results.NoContent();
});

app.MapPost("/api/test/failure", (MockFailureRequest request) =>
{
    failure.Configure(request.StatusCode, request.DelayMilliseconds, request.Count);
    return Results.NoContent();
});

app.Run();

namespace Parkeren.TwoParkMock
{
    public partial class Program { }
}

public sealed record MockActionRequest(string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location);
public sealed record MockExtendRequest(DateTimeOffset End);
public sealed record MockParkingAction(string Id, string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location, string Status);

public sealed record MockFailureRequest(int StatusCode = 503, int DelayMilliseconds = 0, int Count = 1);

public sealed class MockFailureState
{
    private int remaining;
    public int StatusCode { get; private set; } = 503;
    public int DelayMilliseconds { get; private set; }

    public void Configure(int statusCode, int delayMilliseconds, int count)
    {
        StatusCode = statusCode;
        DelayMilliseconds = Math.Max(0, delayMilliseconds);
        Interlocked.Exchange(ref remaining, Math.Max(0, count));
    }

    public void Reset() => Configure(503, 0, 0);

    public async Task<bool> ApplyAsync()
    {
        if (Interlocked.Decrement(ref remaining) < 0)
        {
            Interlocked.Exchange(ref remaining, 0);
            return false;
        }
        if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds);
        return true;
    }
}

public sealed record MockUnknownOutcomeRequest(int StatusCode = 504, int DelayMilliseconds = 0, int Count = 1);

public sealed class MockUnknownOutcomeState
{
    private int remaining;
    public int StatusCode { get; private set; } = 504;
    public int DelayMilliseconds { get; private set; }

    public void Configure(int statusCode, int delayMilliseconds, int count)
    {
        StatusCode = statusCode;
        DelayMilliseconds = Math.Max(0, delayMilliseconds);
        Interlocked.Exchange(ref remaining, Math.Max(0, count));
    }

    public void Reset() => Configure(504, 0, 0);

    public async Task<bool> ApplyAsync()
    {
        if (Interlocked.Decrement(ref remaining) < 0)
        {
            Interlocked.Exchange(ref remaining, 0);
            return false;
        }
        if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds);
        return true;
    }
}

public sealed record MockCapacityRequest(int MaxConcurrentActions);
