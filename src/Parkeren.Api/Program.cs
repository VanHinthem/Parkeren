using Microsoft.AspNetCore.RateLimiting;
using Parkeren.Application.Authentication;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

const string sessionCookie = "parkeren-session";
var secureSessionCookie = !app.Environment.IsDevelopment();

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

public partial class Program;
