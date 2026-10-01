using Parkeren.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Parkeren.Application.Administration;
using Parkeren.Application.Authentication;
using Parkeren.Application.Visits;
using Parkeren.Application.ParkingProvider;
using Parkeren.Domain.Users;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Infrastructure;
using Parkeren.Infrastructure.Persistence;
using Parkeren.Infrastructure.Notifications;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<VisitSchedulerWorker>();
builder.Services.Configure<NotificationRetentionOptions>(builder.Configuration.GetSection("Notifications"));
builder.Services.Configure<WebPushOptions>(builder.Configuration.GetSection("WebPush"));
builder.Services.AddHostedService<NotificationRetentionWorker>();
builder.Services.AddHostedService<PushDeliveryWorker>();
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
    if (app.Environment.IsDevelopment())
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();

    await using (var settingsTransaction = await db.Database.BeginTransactionAsync())
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0x5041524B})");
        if (!await db.ParkingSystemSettings.AnyAsync())
            db.ParkingSystemSettings.Add(new ParkingSystemSettings(Guid.NewGuid(), 5));
        if (!await db.DefaultParkingPolicies.AnyAsync())
            db.DefaultParkingPolicies.Add(new DefaultParkingPolicy(Guid.NewGuid(), TimeSpan.FromHours(4), TimeSpan.FromHours(8), true, false));
        if (!await db.ParkingRuleSets.AnyAsync())
        {
            var paidWindows = Enumerable.Range((int)DayOfWeek.Monday, 6).Select(day => new PaidWindow((DayOfWeek)day, new TimeOnly(9, 0), new TimeOnly(20, 0))).ToArray();
            db.ParkingRuleSets.Add(new ParkingRuleSet(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null, TimeSpan.FromHours(4), paidWindows, true, ProviderCoverageContinuation.StartNewAction));
        }
        await db.SaveChangesAsync();
        await settingsTransaction.CommitAsync();
    }

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
        var adminLimit = await db.ParkingSystemSettings.Select(x => x.MaxConcurrentVisits).SingleAsync();
        var adminPolicy = new UserPolicyOverride(user.Id);
        adminPolicy.SetMaxConcurrentVisits(adminLimit);
        db.UserPolicyOverrides.Add(adminPolicy);
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Initial administrator account created.");
    }

    if (!await db.ParkingProviderProducts.AnyAsync())
    {
        try
        {
            var productCatalog = scope.ServiceProvider.GetRequiredService<IProviderProductCatalogService>();
            var sync = await productCatalog.SynchronizeAsync();
            app.Logger.LogInformation("Initial provider product synchronization completed with {ProductCount} product(s); default auto-selected: {DefaultAutoSelected}.", sync.Products.Count, sync.DefaultAutoSelected);
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Initial provider product synchronization failed. Products can be synchronized manually from administration.");
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapGet("/api/dev/parking-provider", async (IParkingProvider provider, CancellationToken cancellationToken) => Results.Ok(new { categories = await provider.GetCategoriesAsync(cancellationToken), product = await provider.GetProductAsync(cancellationToken), balance = await provider.GetBalanceAsync(cancellationToken), actions = await provider.GetActionsAsync(cancellationToken) }));
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAntiforgery();

const string sessionCookie = "parkeren-session";
var secureSessionCookie = !app.Environment.IsDevelopment();

app.MapGet("/api/auth/csrf", (Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery, HttpContext context) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));

app.MapPost("/api/auth/login", async (LoginRequest request, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var result = await authentication.LoginAsync(request.Username, request.Pin, cancellationToken);
    if (result is null) return Results.Unauthorized();
    SetSessionCookie(context, result.SessionToken, result.ExpiresAt, secureSessionCookie);
    return Results.Ok(result.User);
}).RequireRateLimiting("login");

app.MapGet("/api/admin/system/diagnostics", async (IAdminSystemDiagnosticsService diagnostics, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return Results.Ok(await diagnostics.GetAsync(cancellationToken));
});

app.MapHealthChecks("/health");
app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));
app.MapFallbackToFile("index.html");
app.Run();

async Task<(AuthenticatedUser? User, string? Token)> GetAuthenticatedAsync(IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken)
{
    if (!context.Request.Cookies.TryGetValue(sessionCookie, out var token)) return (null, null);
    return (await authentication.AuthenticateAsync(token, cancellationToken), token);
}

void SetSessionCookie(HttpContext context, string token, DateTimeOffset expiresAt, bool secure) => context.Response.Cookies.Append(sessionCookie, token, new CookieOptions { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Strict, Path = "/", Expires = expiresAt });

public sealed record LoginRequest(string Username, string Pin);
public partial class Program;
