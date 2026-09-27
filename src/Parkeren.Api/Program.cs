using Microsoft.AspNetCore.RateLimiting;
using Parkeren.Application.Authentication;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ParkerenDbContext>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

const string sessionCookie = "__Host-parkeren-session";

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var result = await authentication.LoginAsync(request.Username, request.Pin, cancellationToken);
    if (result is null)
        return Results.Unauthorized();

    context.Response.Cookies.Append(sessionCookie, result.SessionToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = result.ExpiresAt
    });

    return Results.Ok(result.User);
}).RequireRateLimiting("login");

app.MapPost("/api/auth/logout", async (
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (context.Request.Cookies.TryGetValue(sessionCookie, out var token))
        await authentication.LogoutAsync(token, cancellationToken);

    context.Response.Cookies.Delete(sessionCookie, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });

    return Results.NoContent();
});

app.MapGet("/api/auth/me", async (
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (!context.Request.Cookies.TryGetValue(sessionCookie, out var token))
        return Results.Unauthorized();

    var user = await authentication.AuthenticateAsync(token, cancellationToken);
    return user is null ? Results.Unauthorized() : Results.Ok(user);
});

app.MapHealthChecks("/health");
app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();

public sealed record LoginRequest(string Username, string Pin);

public partial class Program;
