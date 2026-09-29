using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var actions = new ConcurrentDictionary<string, MockParkingAction>();
var maxConcurrentActions = 5;
var maxActionDuration = TimeSpan.FromHours(4);
var remainingMinutes = 1500 * 60;
var validCredentials = true;
var visibilityDelay = TimeSpan.Zero;
var forcedValidationError = false;
var rejectDuplicateActiveActions = false;
var omitCreatedActionBody = false;
var categories = new[] { new MockCategory("oss", "Oss") };
var product = new MockProduct("visitor", "Bezoekersparkeren", "Oss");
var failure = new MockFailureState();
var outcome = new MockUnknownOutcomeState();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "2park-mock" }));

app.MapGet("/api/categories", () => Results.Ok(categories));
app.MapGet("/api/product", () => Results.Ok(product));

app.MapGet("/api/balance", async () =>
{
    if (!validCredentials) return Results.Unauthorized();
    if (await failure.ApplyAsync()) return Results.StatusCode(failure.StatusCode);
    return Results.Ok(new
{
    remainingPaidMinutes = remainingMinutes,
    retrievedAt = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/actions", () => Results.Ok(actions.Values
    .Where(x => DateTimeOffset.UtcNow >= x.VisibleAt)
    .OrderBy(x => x.Start)));

app.MapPost("/api/actions", async (MockActionRequest request) =>
{
    if (!validCredentials) return Results.Unauthorized();
    if (await failure.ApplyAsync()) return Results.StatusCode(failure.StatusCode);
    if (forcedValidationError)
        return Results.BadRequest(new { error = "Provider validation rejected the action." });

    if (request.End <= request.Start)
        return Results.BadRequest(new { error = "End must be after start." });

    if (request.End - request.Start > maxActionDuration)
        return Results.BadRequest(new { error = "Provider action exceeds maximum duration." });

    if ((request.End - request.Start).TotalMinutes > remainingMinutes)
        return Results.Conflict(new { error = "Insufficient provider balance." });

    if (rejectDuplicateActiveActions && actions.Values.Any(x => x.Status == "active" && x.End > DateTimeOffset.UtcNow && x.LicensePlate == request.LicensePlate))
        return Results.Conflict(new { error = "Duplicate active provider action." });

    if (actions.Values.Count(x => x.Status == "active" && x.End > DateTimeOffset.UtcNow) >= maxConcurrentActions)
        return Results.Conflict(new { error = "Provider capacity reached." });

    var id = Guid.NewGuid().ToString("N");
    var status = request.Start > DateTimeOffset.UtcNow ? "scheduled" : "active";
    var action = new MockParkingAction(id, request.LicensePlate, request.Start, request.End, request.Location, status, DateTimeOffset.UtcNow + visibilityDelay);
    actions[id] = action;
    if (await outcome.ApplyAsync()) return Results.StatusCode(outcome.StatusCode);
    if (omitCreatedActionBody) return Results.Created($"/api/actions/{id}", value: null);
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

app.MapPost("/api/test/actions/{id}/stop", (string id) =>
{
    if (!actions.TryGetValue(id, out var current)) return Results.NotFound();
    actions[id] = current with { Status = "stopped" };
    return Results.NoContent();
});

app.MapPut("/api/test/actions/{id}/end", (string id, MockExtendRequest request) =>
{
    if (!actions.TryGetValue(id, out var current)) return Results.NotFound();
    actions[id] = current with { End = request.End };
    return Results.NoContent();
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

app.MapPost("/api/test/omit-created-body", (MockOmitCreatedBodyRequest request) =>
{
    omitCreatedActionBody = request.Enabled;
    return Results.NoContent();
});

app.MapPost("/api/test/reject-duplicates", (MockDuplicateRequest request) =>
{
    rejectDuplicateActiveActions = request.Enabled;
    return Results.NoContent();
});

app.MapPost("/api/test/validation-error", (MockValidationErrorRequest request) =>
{
    forcedValidationError = request.Enabled;
    return Results.NoContent();
});

app.MapPost("/api/test/visibility-delay", (MockVisibilityDelayRequest request) =>
{
    visibilityDelay = TimeSpan.FromMilliseconds(Math.Max(0, request.Milliseconds));
    return Results.NoContent();
});

app.MapPost("/api/test/authentication", (MockAuthenticationRequest request) =>
{
    validCredentials = request.Valid;
    return Results.NoContent();
});

app.MapPost("/api/test/balance", (MockBalanceRequest request) =>
{
    remainingMinutes = Math.Max(0, request.RemainingPaidMinutes);
    return Results.NoContent();
});

app.MapPost("/api/test/max-action-duration", (MockMaxActionDurationRequest request) =>
{
    maxActionDuration = TimeSpan.FromMinutes(Math.Max(1, request.Minutes));
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
    maxActionDuration = TimeSpan.FromHours(4);
    remainingMinutes = 1500 * 60;
    validCredentials = true;
    visibilityDelay = TimeSpan.Zero;
    forcedValidationError = false;
    rejectDuplicateActiveActions = false;
    omitCreatedActionBody = false;
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
public sealed record MockParkingAction(string Id, string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location, string Status, DateTimeOffset VisibleAt);

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

public sealed record MockMaxActionDurationRequest(int Minutes);

public sealed record MockBalanceRequest(int RemainingPaidMinutes);

public sealed record MockAuthenticationRequest(bool Valid);

public sealed record MockVisibilityDelayRequest(int Milliseconds);

public sealed record MockValidationErrorRequest(bool Enabled);

public sealed record MockDuplicateRequest(bool Enabled);

public sealed record MockOmitCreatedBodyRequest(bool Enabled);

public sealed record MockCategory(string Id, string Name);
public sealed record MockProduct(string Id, string Name, string Location);
