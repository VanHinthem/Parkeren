using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var actions = new ConcurrentDictionary<string, MockParkingAction>();
var remainingMinutes = 1500 * 60;

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "2park-mock" }));

app.MapGet("/api/balance", () => Results.Ok(new
{
    remainingPaidMinutes = remainingMinutes,
    retrievedAt = DateTimeOffset.UtcNow
}));

app.MapGet("/api/actions", () => Results.Ok(actions.Values.OrderBy(x => x.Start)));

app.MapPost("/api/actions", (MockActionRequest request) =>
{
    if (request.End <= request.Start)
        return Results.BadRequest(new { error = "End must be after start." });

    var id = Guid.NewGuid().ToString("N");
    var action = new MockParkingAction(id, request.LicensePlate, request.Start, request.End, request.Location, "active");
    actions[id] = action;
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

app.Run();

public sealed record MockActionRequest(string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location);
public sealed record MockExtendRequest(DateTimeOffset End);
public sealed record MockParkingAction(string Id, string LicensePlate, DateTimeOffset Start, DateTimeOffset End, string Location, string Status);
