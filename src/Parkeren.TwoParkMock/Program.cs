using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var statePath = builder.Configuration["TwoParkMock:StatePath"];
var stateGate = new SemaphoreSlim(1, 1);
var mockClock = new MockClock();
var actions = new ConcurrentDictionary<string, MockParkingAction>(
    LoadPersistedActions(statePath).ToDictionary(x => x.Id, StringComparer.Ordinal));

async Task PersistActionsAsync()
{
    if (string.IsNullOrWhiteSpace(statePath))
        return;

    await stateGate.WaitAsync();
    try
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var snapshot = actions.Values.OrderBy(x => x.Start).ToArray();
        var json = JsonSerializer.Serialize(snapshot);
        var temporaryPath = statePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json);
        File.Move(temporaryPath, statePath, true);
    }
    finally
    {
        stateGate.Release();
    }
}

static IReadOnlyCollection<MockParkingAction> LoadPersistedActions(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return Array.Empty<MockParkingAction>();

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<MockParkingAction[]>(json) ?? Array.Empty<MockParkingAction>();
}

static MockParkingAction WithObservableStatus(MockParkingAction action, DateTimeOffset now)
{
    if (string.Equals(action.Status, "stopped", StringComparison.OrdinalIgnoreCase))
        return action;

    if (now < action.Start)
        return action.Status == "scheduled" ? action : action with { Status = "scheduled" };

    if (now < action.End)
        return action.Status == "active" ? action : action with { Status = "active" };

    // Post-End provider semantics are intentionally not inferred until live 2Park behavior is known.
    return action;
}

var maxConcurrentActions = 5;
var maxActionDuration = TimeSpan.FromHours(4);
const string defaultProductId = "visitor";
var remainingMinutesByProduct = new ConcurrentDictionary<string, int>(
    new[] { new KeyValuePair<string, int>(defaultProductId, 1500 * 60) });
var validCredentials = true;
var visibilityDelay = TimeSpan.Zero;
var forcedValidationError = false;
var rejectDuplicateActiveActions = false;
var omitCreatedActionBody = false;
var categories = new[] { new MockCategory("oss", "Oss") };
var products = new[]
{
    new MockProduct(defaultProductId, "Bezoekersparkeren", "OSS_J", "oss", "Oss")
};
var failure = new MockFailureState();
var outcome = new MockUnknownOutcomeState();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "2park-mock" }));

app.MapGet("/api/categories", () => Results.Ok(categories));
app.MapGet("/api/products", () => Results.Ok(products));
app.MapGet("/api/product", () => Results.Ok(products.Single()));

app.MapGet("/api/balance", async (string? productId) =>
{
    if (!validCredentials) return Results.Unauthorized();
    if (await failure.ApplyAsync()) return Results.StatusCode(failure.StatusCode);

    var selectedProductId = string.IsNullOrWhiteSpace(productId) ? defaultProductId : productId;
    if (!remainingMinutesByProduct.TryGetValue(selectedProductId, out var remainingMinutes))
        return Results.NotFound();

    return Results.Ok(new
    {
        remainingPaidMinutes = remainingMinutes,
        retrievedAt = mockClock.UtcNow
    });
});

app.MapGet("/api/actions", (string? productId) =>
{
    var selectedProductId = string.IsNullOrWhiteSpace(productId) ? defaultProductId : productId;
    if (!products.Any(x => x.Id == selectedProductId))
        return Results.NotFound();

    var now = mockClock.UtcNow;
    return Results.Ok(actions.Values
        .Where(x => x.ProductId == selectedProductId && now >= x.VisibleAt)
        .Select(x => WithObservableStatus(x, now))
        .OrderBy(x => x.Start));
});

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

    var selectedProductId = string.IsNullOrWhiteSpace(request.ProductId) ? defaultProductId : request.ProductId;
    if (!remainingMinutesByProduct.TryGetValue(selectedProductId, out var remainingMinutes))
        return Results.NotFound();

    if ((request.End - request.Start).TotalMinutes > remainingMinutes)
        return Results.Conflict(new { error = "Insufficient provider balance." });

    if (rejectDuplicateActiveActions && actions.Values.Any(x => x.Status == "active" && x.End > mockClock.UtcNow && x.LicensePlate == request.LicensePlate))
        return Results.Conflict(new { error = "Duplicate active provider action." });

    if (actions.Values.Count(x => x.Status == "active" && x.End > mockClock.UtcNow) >= maxConcurrentActions)
        return Results.Conflict(new { error = "Provider capacity reached." });

    var id = Guid.NewGuid().ToString("N");
    var status = request.Start > mockClock.UtcNow ? "scheduled" : "active";
    var action = new MockParkingAction(id, request.LicensePlate, request.Start, request.End, request.Location, status, mockClock.UtcNow + visibilityDelay, selectedProductId);
    actions[id] = action;
    await PersistActionsAsync();
    if (await outcome.ApplyAsync()) return Results.StatusCode(outcome.StatusCode);
    if (omitCreatedActionBody) return Results.Created($"/api/actions/{id}", value: null);
    return Results.Created($"/api/actions/{id}", action);
});

app.MapPut("/api/actions/{id}/end", async (string id, string? productId, MockExtendRequest request) =>
{
    if (!actions.TryGetValue(id, out var current))
        return Results.NotFound();
    if (!string.IsNullOrWhiteSpace(productId) && current.ProductId != productId)
        return Results.NotFound();

    if (request.End <= current.Start)
        return Results.BadRequest(new { error = "End must be after start." });

    var updated = current with { End = request.End };
    actions[id] = updated;
    await PersistActionsAsync();
    return Results.Ok(updated);
});

app.MapPost("/api/test/actions/{id}/stop", async (string id) =>
{
    if (!actions.TryGetValue(id, out var current)) return Results.NotFound();
    actions[id] = current with { Status = "stopped" };
    await PersistActionsAsync();
    return Results.NoContent();
});

app.MapPut("/api/test/actions/{id}/end", async (string id, MockExtendRequest request) =>
{
    if (!actions.TryGetValue(id, out var current)) return Results.NotFound();
    actions[id] = current with { End = request.End };
    await PersistActionsAsync();
    return Results.NoContent();
});

app.MapPost("/api/actions/{id}/stop", async (string id, string? productId) =>
{
    if (!actions.TryGetValue(id, out var current))
        return Results.NotFound();
    if (!string.IsNullOrWhiteSpace(productId) && current.ProductId != productId)
        return Results.NotFound();

    actions[id] = current with { Status = "stopped" };
    await PersistActionsAsync();
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
    remainingMinutesByProduct[defaultProductId] = Math.Max(0, request.RemainingPaidMinutes);
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

app.MapPost("/api/test/clock/set", (MockClockSetRequest request) =>
{
    mockClock.Set(request.UtcNow);
    return Results.Ok(new { utcNow = mockClock.UtcNow });
});

app.MapPost("/api/test/clock/advance", (MockClockAdvanceRequest request) =>
{
    if (request.Milliseconds < 0)
        return Results.BadRequest(new { error = "Clock advance must be non-negative." });
    mockClock.Advance(TimeSpan.FromMilliseconds(request.Milliseconds));
    return Results.Ok(new { utcNow = mockClock.UtcNow });
});

app.MapPost("/api/test/clock/reset", () =>
{
    mockClock.Reset();
    return Results.Ok(new { utcNow = mockClock.UtcNow });
});

app.MapPost("/api/test/reset", async () =>
{
    actions.Clear();
    failure.Reset();
    outcome.Reset();
    maxConcurrentActions = 5;
    maxActionDuration = TimeSpan.FromHours(4);
    remainingMinutesByProduct.Clear();
    remainingMinutesByProduct[defaultProductId] = 1500 * 60;
    validCredentials = true;
    visibilityDelay = TimeSpan.Zero;
    forcedValidationError = false;
    rejectDuplicateActiveActions = false;
    omitCreatedActionBody = false;
    mockClock.Reset();
    await PersistActionsAsync();
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

public sealed record MockActionRequest(
    string LicensePlate,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    string? ProductId = null);
public sealed record MockExtendRequest(DateTimeOffset End);
public sealed record MockParkingAction(
    string Id,
    string LicensePlate,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    string Status,
    DateTimeOffset VisibleAt,
    string ProductId = "visitor");

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

public sealed record MockClockSetRequest(DateTimeOffset UtcNow);
public sealed record MockClockAdvanceRequest(double Milliseconds);

public sealed record MockCapacityRequest(int MaxConcurrentActions);

public sealed record MockMaxActionDurationRequest(int Minutes);

public sealed record MockBalanceRequest(int RemainingPaidMinutes);

public sealed record MockAuthenticationRequest(bool Valid);

public sealed record MockVisibilityDelayRequest(int Milliseconds);

public sealed record MockValidationErrorRequest(bool Enabled);

public sealed record MockDuplicateRequest(bool Enabled);

public sealed record MockOmitCreatedBodyRequest(bool Enabled);

public sealed record MockCategory(string Id, string Name);
public sealed record MockProduct(
    string Id,
    string Name,
    string Location,
    string? CategoryId = null,
    string? CategoryName = null);
