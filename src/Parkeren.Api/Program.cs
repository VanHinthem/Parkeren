using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Parkeren.Application.Administration;
using Parkeren.Application.Authentication;
using Parkeren.Application.Visits;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<ParkerenDbContext>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ParkerenDbContext>();
    await db.Database.MigrateAsync();

    if (!await db.Users.AnyAsync())
    {
        var username = app.Configuration["BootstrapAdmin:Username"];
        var pin = app.Configuration["BootstrapAdmin:Pin"];

        if (string.IsNullOrWhiteSpace(username) || pin is null || pin.Length != 6 || !pin.All(char.IsAsciiDigit))
            throw new InvalidOperationException("No users exist. Configure BootstrapAdmin__Username and a six-digit BootstrapAdmin__Pin.");

        var trimmedUsername = username.Trim();
        var user = new User(Guid.NewGuid(), trimmedUsername, trimmedUsername.ToUpperInvariant(), string.Empty, UserRole.Admin);
        var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
        user.ChangePinHash(hasher.HashPassword(user, pin));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Initial administrator account created.");
    }
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAntiforgery();

const string sessionCookie = "parkeren-session";
var secureSessionCookie = !app.Environment.IsDevelopment();

app.MapGet("/api/auth/csrf", (Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
});

app.MapPost("/api/auth/login", async (
    LoginRequest request, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var result = await authentication.LoginAsync(request.Username, request.Pin, cancellationToken);
    if (result is null)
        return Results.Unauthorized();

    SetSessionCookie(context, result.SessionToken, result.ExpiresAt, secureSessionCookie);
    return Results.Ok(result.User);
}).RequireRateLimiting("login");

app.MapPost("/api/auth/logout", async (
    IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    if (context.Request.Cookies.TryGetValue(sessionCookie, out var token))
        await authentication.LogoutAsync(token, cancellationToken);

    DeleteSessionCookie(context, secureSessionCookie);
    return Results.NoContent();
});

app.MapGet("/api/auth/me", async (
    IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    return authenticated.User is null ? Results.Unauthorized() : Results.Ok(authenticated.User);
});

app.MapPost("/api/auth/change-pin", async (
    ChangePinRequest request, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    if (!context.Request.Cookies.TryGetValue(sessionCookie, out var token))
        return Results.Unauthorized();

    var user = await authentication.AuthenticateAsync(token, cancellationToken);
    if (user is null)
        return Results.Unauthorized();

    return await authentication.ChangePinAsync(token, request.CurrentPin, request.NewPin, cancellationToken)
        ? Results.NoContent()
        : Results.BadRequest(new { error = "PIN kon niet worden gewijzigd." });
});

app.MapPost("/api/admin/users/{userId:guid}/reset-pin", async (
    Guid userId, ResetPinRequest request, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return await authentication.ResetPinAsync(authenticated.User.Id, userId, request.NewPin, cancellationToken)
        ? Results.NoContent()
        : Results.BadRequest(new { error = "PIN kon niet worden gereset." });
});

app.MapPost("/api/admin/users/{userId:guid}/revoke-sessions", async (
    Guid userId, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return await authentication.RevokeAllSessionsAsync(authenticated.User.Id, userId, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
});

app.MapPost("/api/visits/start", async (
    StartVisitRequest request,
    StartVisitFlow flow,
    IStartVisitRequestResolver requestResolver,
    IStartVisitOperationalContextResolver operationalContextResolver,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    if (request.OperationId == Guid.Empty || request.VehicleId == Guid.Empty)
        return Results.BadRequest(new { error = "OperationId en VehicleId zijn verplicht." });

    var ownerUserId = request.OwnerUserId ?? authenticated.User.Id;
    var startAt = DateTimeOffset.UtcNow;

    var requestContext = await requestResolver.ResolveAsync(
        authenticated.User.Id,
        ownerUserId,
        request.VehicleId,
        cancellationToken);
    if (requestContext is null)
        return Results.BadRequest(new { error = "Visit-context kon niet worden bepaald." });

    var operationalContext = await operationalContextResolver.ResolveAsync(
        ownerUserId,
        startAt,
        request.DesiredEndAt,
        cancellationToken);
    if (operationalContext is null)
        return Results.Problem("Parkeerbeleid of parkeerregels zijn niet beschikbaar.", statusCode: StatusCodes.Status503ServiceUnavailable);

    var command = new StartVisitCommand(
        request.OperationId,
        ownerUserId,
        authenticated.User.Id,
        request.VehicleId,
        startAt,
        request.DesiredEndAt);

    try
    {
        var result = await flow.StartAsync(
            command,
            requestContext.StartContext,
            operationalContext.Policy,
            operationalContext.RuleSets,
            operationalContext.CoverageEvaluationEndAt,
            operationalContext.MaxConcurrentVisits,
            requestContext.ProviderContext,
            cancellationToken);

        if (result is null)
            return Results.Conflict(new { error = "Er is geen parkeercapaciteit beschikbaar." });

        return result.Outcome switch
        {
            StartVisitFlowOutcome.ReconciliationRequired => Results.Accepted($"/api/visits/{result.Visit.Id}", result),
            StartVisitFlowOutcome.DefinitiveFailure => Results.Conflict(result),
            _ => Results.Ok(result)
        };
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapPost("/api/visits/{visitId:guid}/stop", async (
    Guid visitId,
    StopVisitRequest request,
    StopVisitFlow flow,
    IStopVisitRequestResolver requestResolver,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    if (request.OperationId == Guid.Empty)
        return Results.BadRequest(new { error = "OperationId is verplicht." });

    var stopContext = await requestResolver.ResolveAsync(
        authenticated.User.Id,
        visitId,
        cancellationToken);
    if (stopContext is null)
        return Results.NotFound();

    var command = new StopVisitCommand(
        request.OperationId,
        visitId,
        authenticated.User.Id);

    try
    {
        var result = await flow.StopAsync(command, stopContext, cancellationToken);
        return result.Outcome == StopVisitFlowOutcome.ReconciliationRequired
            ? Results.Accepted($"/api/visits/{visitId}", result)
            : Results.Ok(result);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapPut("/api/visits/{visitId:guid}/end-time", async (
    Guid visitId,
    ChangeVisitEndTimeRequest request,
    ChangeVisitEndTimeFlow flow,
    IStopVisitRequestResolver requestResolver,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    if (request.OperationId == Guid.Empty)
        return Results.BadRequest(new { error = "OperationId is verplicht." });

    var visitContext = await requestResolver.ResolveAsync(
        authenticated.User.Id,
        visitId,
        cancellationToken);
    if (visitContext is null)
        return Results.NotFound();

    var command = new ChangeVisitEndTimeCommand(
        request.OperationId,
        visitId,
        authenticated.User.Id,
        request.DesiredEndAt);

    try
    {
        var result = await flow.ChangeAsync(command, visitContext, cancellationToken);
        return result.Outcome == ChangeVisitEndTimeFlowOutcome.ReconciliationRequired
            ? Results.Accepted($"/api/visits/{visitId}", result)
            : Results.Ok(result);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapGet("/api/visits/active", async (
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var visit = await dbContext.Visits
        .AsNoTracking()
        .Where(x => x.UserId == authenticated.User.Id)
        .Where(x => x.Status == VisitStatus.Starting || x.Status == VisitStatus.Active || x.Status == VisitStatus.Stopping)
        .OrderByDescending(x => x.StartAt)
        .FirstOrDefaultAsync(cancellationToken);

    return visit is null ? Results.NotFound() : Results.Ok(visit);
});

app.MapGet("/api/visits/{visitId:guid}", async (
    Guid visitId,
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var visit = await dbContext.Visits
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == visitId, cancellationToken);
    if (visit is null)
        return Results.NotFound();

    if (visit.UserId != authenticated.User.Id && authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return Results.Ok(visit);
});

app.MapGet("/api/vehicles", async (
    IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var vehicles = await administration.GetAuthorizedVehiclesAsync(authenticated.User.Id, cancellationToken);
    return vehicles is null ? Results.Unauthorized() : Results.Ok(vehicles);
});

app.MapGet("/api/admin/users", async (
    IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return Results.Ok(await administration.GetUsersAsync(authenticated.User.Id, cancellationToken));
});

app.MapPost("/api/admin/users", async (
    CreateUserRequest request, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    var created = await administration.CreateUserAsync(authenticated.User.Id, request.Username, request.Pin, request.Role, cancellationToken);
    return created is null ? Results.BadRequest() : Results.Created($"/api/admin/users/{created.Id}", created);
});

app.MapPut("/api/admin/users/{userId:guid}/active", async (
    Guid userId, SetActiveRequest request, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return await administration.SetUserActiveAsync(authenticated.User.Id, userId, request.IsActive, cancellationToken) ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/admin/vehicles", async (
    IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return Results.Ok(await administration.GetVehiclesAsync(authenticated.User.Id, cancellationToken));
});

app.MapPost("/api/admin/vehicles", async (
    CreateVehicleRequest request, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    var created = await administration.CreateVehicleAsync(authenticated.User.Id, request.LicensePlate, request.DisplayName, cancellationToken);
    return created is null ? Results.BadRequest() : Results.Created($"/api/admin/vehicles/{created.Id}", created);
});

app.MapPut("/api/admin/vehicles/{vehicleId:guid}/active", async (
    Guid vehicleId, SetActiveRequest request, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return await administration.SetVehicleActiveAsync(authenticated.User.Id, vehicleId, request.IsActive, cancellationToken) ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/admin/users/{userId:guid}/vehicles", async (
    Guid userId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    var vehicles = await administration.GetAssignedVehiclesAsync(authenticated.User.Id, userId, cancellationToken);
    return vehicles is null ? Results.NotFound() : Results.Ok(vehicles);
});

app.MapPut("/api/admin/users/{userId:guid}/vehicles/{vehicleId:guid}", async (
    Guid userId, Guid vehicleId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return await administration.AssignVehicleAsync(authenticated.User.Id, userId, vehicleId, cancellationToken) ? Results.NoContent() : Results.NotFound();
});

app.MapDelete("/api/admin/users/{userId:guid}/vehicles/{vehicleId:guid}", async (
    Guid userId, Guid vehicleId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return await administration.UnassignVehicleAsync(authenticated.User.Id, userId, vehicleId, cancellationToken) ? Results.NoContent() : Results.NotFound();
});

app.MapHealthChecks("/health");
app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));
app.MapFallbackToFile("index.html");

app.Run();

async Task<(AuthenticatedUser? User, string? Token)> GetAuthenticatedAsync(
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (!context.Request.Cookies.TryGetValue(sessionCookie, out var token))
        return (null, null);

    return (await authentication.AuthenticateAsync(token, cancellationToken), token);
}

void SetSessionCookie(HttpContext context, string token, DateTimeOffset expiresAt, bool secure)
{
    context.Response.Cookies.Append(sessionCookie, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt
    });
}

void DeleteSessionCookie(HttpContext context, bool secure)
{
    context.Response.Cookies.Delete(sessionCookie, new CookieOptions
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });
}

public sealed record LoginRequest(string Username, string Pin);
public sealed record ChangePinRequest(string CurrentPin, string NewPin);
public sealed record ResetPinRequest(string NewPin);
public sealed record CreateUserRequest(string Username, string Pin, UserRole Role);
public sealed record CreateVehicleRequest(string LicensePlate, string? DisplayName);
public sealed record SetActiveRequest(bool IsActive);
public sealed record StartVisitRequest(Guid OperationId, Guid VehicleId, Guid? OwnerUserId, DateTimeOffset? DesiredEndAt);
public sealed record StopVisitRequest(Guid OperationId);
public sealed record ChangeVisitEndTimeRequest(Guid OperationId, DateTimeOffset? DesiredEndAt);

public partial class Program;
