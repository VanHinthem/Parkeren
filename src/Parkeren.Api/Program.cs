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
using Parkeren.Infrastructure.ParkingProvider;
using Parkeren.Infrastructure.Notifications;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<FailedSchedulerWorkReleaseQueue>();
builder.Services.AddHostedService<VisitSchedulerWorker>();
builder.Services.AddHostedService<ProviderHistorySyncWorker>();
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
    await db.Database.MigrateAsync();

    await using (var settingsTransaction = await db.Database.BeginTransactionAsync())
    {
        // Serialize the singleton seed with capacity claims and administration writes.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0x5041524B})");
        if (!await db.ParkingSystemSettings.AnyAsync())
        {
            db.ParkingSystemSettings.Add(new ParkingSystemSettings(Guid.NewGuid(), 5));
        }

        if (!await db.DefaultParkingPolicies.AnyAsync())
        {
            db.DefaultParkingPolicies.Add(new DefaultParkingPolicy(
                Guid.NewGuid(),
                TimeSpan.FromHours(4),
                TimeSpan.FromHours(8),
                allowVisitExtension: true,
                allowOpenEndedVisits: false));
        }

        if (!await db.ParkingRuleSets.AnyAsync())
        {
            var paidWindows = Enumerable.Range((int)DayOfWeek.Monday, 6)
                .Select(day => new PaidWindow(
                    (DayOfWeek)day,
                    new TimeOnly(9, 0),
                    new TimeOnly(20, 0)))
                .ToArray();

            db.ParkingRuleSets.Add(new ParkingRuleSet(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                validUntil: null,
                TimeSpan.FromHours(4),
                paidWindows,
                publicHolidaysAreFree: true,
                continuation: ProviderCoverageContinuation.StartNewAction));
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

    await ProgramStartupTasks.SynchronizeProviderProductsAsync(scope.ServiceProvider, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.MapGet("/api/dev/parking-provider", async (
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        var categories = await provider.GetCategoriesAsync(cancellationToken);
        var product = await provider.GetProductAsync(cancellationToken);
        var balance = await provider.GetBalanceAsync(cancellationToken);
        var actions = await provider.GetActionsAsync(cancellationToken);

        return Results.Ok(new
        {
            product,
            categories,
            balance,
            actions
        });
    });

    app.MapPost("/api/dev/parking-provider/actions/start", async (
        DevStartProviderActionRequest request,
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
            return Results.BadRequest(new { error = "LicensePlate is verplicht." });
        var isShortDiagnostic = request.DurationMinutes is >= 1 and <= 15;
        var isProviderDurationBoundaryDiagnostic = request.DurationMinutes is 240 or 241;
        if (!isShortDiagnostic && !isProviderDurationBoundaryDiagnostic)
            return Results.BadRequest(new { error = "DurationMinutes moet 1-15, 240 of 241 zijn voor de diagnostische test." });

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Location))
            return Results.Problem("2Park-locatie kon niet worden bepaald.", statusCode: StatusCodes.Status503ServiceUnavailable);

        if (request.StartInMinutes is < 0 or > 15)
            return Results.BadRequest(new { error = "StartInMinutes moet voor de diagnostische test tussen 0 en 15 liggen." });

        var start = DateTimeOffset.UtcNow.AddMinutes(request.StartInMinutes);
        var created = await provider.StartActionAsync(
            new ProviderParkingActionRequest(
                request.LicensePlate,
                start,
                start.AddMinutes(request.DurationMinutes),
                product.Location),
            cancellationToken);

        var actions = await provider.GetActionsAsync(cancellationToken);
        return Results.Ok(new { created, actions });
    });

    app.MapPost("/api/dev/parking-provider/actions/test-adjacent", async (
        DevAdjacentProviderActionsRequest request,
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
            return Results.BadRequest(new { error = "LicensePlate is verplicht." });
        if (request.FirstDurationMinutes is < 1 or > 15 || request.SecondDurationMinutes is < 1 or > 15)
            return Results.BadRequest(new { error = "Beide durations moeten voor de diagnostische test tussen 1 en 15 minuten liggen." });

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Location))
            return Results.Problem("2Park-locatie kon niet worden bepaald.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var firstStart = DateTimeOffset.UtcNow;
        var firstCreated = await provider.StartActionAsync(
            new ProviderParkingActionRequest(
                request.LicensePlate,
                firstStart,
                firstStart.AddMinutes(request.FirstDurationMinutes),
                product.Location),
            cancellationToken);

        var actionsAfterFirst = await provider.GetActionsAsync(cancellationToken);
        var firstReadBack = actionsAfterFirst.FirstOrDefault(x => x.ProviderActionId == firstCreated.ProviderActionId);
        if (firstReadBack is null)
            return Results.Problem("De eerste 2Park-actie kon niet worden teruggelezen.", statusCode: StatusCodes.Status502BadGateway);

        var secondStart = firstReadBack.End.AddSeconds(1);
        var secondCreated = await provider.StartActionAsync(
            new ProviderParkingActionRequest(
                request.LicensePlate,
                secondStart,
                secondStart.AddMinutes(request.SecondDurationMinutes),
                product.Location),
            cancellationToken);

        var actions = await provider.GetActionsAsync(cancellationToken);
        return Results.Ok(new { first = firstReadBack, second = secondCreated, actions });
    });

    app.MapPost("/api/dev/parking-provider/actions/test-active-extension", async (
        DevActiveExtensionRequest request,
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
            return Results.BadRequest(new { error = "LicensePlate is verplicht." });
        if (request.InitialDurationMinutes is < 1 or > 15)
            return Results.BadRequest(new { error = "InitialDurationMinutes moet tussen 1 en 15 liggen." });
        if (request.ExtendedDurationMinutes <= request.InitialDurationMinutes)
            return Results.BadRequest(new { error = "ExtendedDurationMinutes moet groter zijn dan InitialDurationMinutes." });
        if (request.ExtendedDurationMinutes is > 241)
            return Results.BadRequest(new { error = "ExtendedDurationMinutes mag voor deze diagnostische test maximaal 241 zijn." });

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Location))
            return Results.Problem("2Park-locatie kon niet worden bepaald.", statusCode: StatusCodes.Status503ServiceUnavailable);

        Parkeren.Application.ParkingProvider.ProviderParkingAction? created = null;
        try
        {
            var start = DateTimeOffset.UtcNow;
            var originalEnd = start.AddMinutes(request.InitialDurationMinutes);
            var requestedEnd = start.AddMinutes(request.ExtendedDurationMinutes);

            created = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    request.LicensePlate,
                    start,
                    originalEnd,
                    product.Location),
                cancellationToken);

            var before = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

            try
            {
                string? rawExtendResponse = null;
                Parkeren.Application.ParkingProvider.ProviderParkingAction changed;

                if (provider is Parkeren.Infrastructure.ParkingProvider.TwoParkProvider twoParkProvider)
                {
                    rawExtendResponse = await twoParkProvider.ExtendActionDiagnosticAsync(
                        created.ProviderActionId,
                        requestedEnd,
                        cancellationToken);

                    changed = (await provider.GetActionsAsync(cancellationToken))
                        .Single(x => x.ProviderActionId == created.ProviderActionId);
                }
                else
                {
                    changed = await provider.ExtendActionAsync(
                        created.ProviderActionId,
                        requestedEnd,
                        cancellationToken);
                }

                var afterImmediate = (await provider.GetActionsAsync(cancellationToken))
                    .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                var afterOneSecond = (await provider.GetActionsAsync(cancellationToken))
                    .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

                await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
                var afterFiveSeconds = (await provider.GetActionsAsync(cancellationToken))
                    .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

                static bool MatchesRequestedEnd(
                    Parkeren.Application.ParkingProvider.ProviderParkingAction? action,
                    DateTimeOffset expectedEnd) =>
                    action is not null &&
                    (action.End - expectedEnd).Duration() < TimeSpan.FromSeconds(1);

                return Results.Ok(new
                {
                    requested = new { start, originalEnd, requestedEnd },
                    created,
                    before,
                    rawExtendResponse,
                    changed,
                    afterImmediate,
                    afterOneSecond,
                    afterFiveSeconds,
                    extensionApplied =
                        MatchesRequestedEnd(afterImmediate, requestedEnd) ||
                        MatchesRequestedEnd(afterOneSecond, requestedEnd) ||
                        MatchesRequestedEnd(afterFiveSeconds, requestedEnd)
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
            {
                var afterFailure = (await provider.GetActionsAsync(CancellationToken.None))
                    .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

                return Results.Ok(new
                {
                    requested = new { start, originalEnd, requestedEnd },
                    created,
                    before,
                    extensionApplied = false,
                    rejected = true,
                    error = exception.Message,
                    after = afterFailure
                });
            }
        }
        finally
        {
            if (created is not null)
            {
                await DiagnosticActionCleanup.StopAsync(
                    () => provider.StopActionAsync(created.ProviderActionId, CancellationToken.None),
                    app.Logger,
                    "Failed to clean up active-extension diagnostic action {ProviderActionId}.",
                    created.ProviderActionId);
            }
        }
    });

    app.MapPost("/api/dev/parking-provider/actions/test-jit-extension", async (
        DevJitExtensionRequest request,
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
            return Results.BadRequest(new { error = "LicensePlate is verplicht." });
        if (request.InitialDurationMinutes is < 2 or > 15)
            return Results.BadRequest(new { error = "InitialDurationMinutes moet tussen 2 en 15 liggen." });
        if (request.ExtendBeforeEndSeconds is < 30 or > 120)
            return Results.BadRequest(new { error = "ExtendBeforeEndSeconds moet tussen 30 en 120 liggen." });
        if (request.ExtensionMinutes is < 1 or > 15)
            return Results.BadRequest(new { error = "ExtensionMinutes moet tussen 1 en 15 liggen." });

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Location))
            return Results.Problem("2Park-locatie kon niet worden bepaald.", statusCode: StatusCodes.Status503ServiceUnavailable);

        Parkeren.Application.ParkingProvider.ProviderParkingAction? created = null;
        try
        {
            var start = DateTimeOffset.UtcNow;
            var originalEnd = start.AddMinutes(request.InitialDurationMinutes);
            var requestedEnd = originalEnd.AddMinutes(request.ExtensionMinutes);

            created = await provider.StartActionAsync(
                new ProviderParkingActionRequest(request.LicensePlate, start, originalEnd, product.Location),
                cancellationToken);

            var extendAt = created.End.AddSeconds(-request.ExtendBeforeEndSeconds);
            var delay = extendAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            var beforeExtend = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

            string? rawExtendResponse = null;
            if (provider is Parkeren.Infrastructure.ParkingProvider.TwoParkProvider twoParkProvider)
            {
                rawExtendResponse = await twoParkProvider.ExtendActionDiagnosticAsync(
                    created.ProviderActionId,
                    requestedEnd,
                    cancellationToken);
            }
            else
            {
                await provider.ExtendActionAsync(created.ProviderActionId, requestedEnd, cancellationToken);
            }

            var afterImmediate = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var afterOneSecond = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            var afterFiveSeconds = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

            static bool MatchesEnd(
                Parkeren.Application.ParkingProvider.ProviderParkingAction? action,
                DateTimeOffset expectedEnd) =>
                action is not null && (action.End - expectedEnd).Duration() < TimeSpan.FromSeconds(1);

            return Results.Ok(new
            {
                requested = new { start, originalEnd, requestedEnd, extendAt },
                created,
                beforeExtend,
                rawExtendResponse,
                afterImmediate,
                afterOneSecond,
                afterFiveSeconds,
                extensionApplied =
                    MatchesEnd(afterImmediate, requestedEnd) ||
                    MatchesEnd(afterOneSecond, requestedEnd) ||
                    MatchesEnd(afterFiveSeconds, requestedEnd)
            });
        }
        finally
        {
            if (created is not null)
            {
                await DiagnosticActionCleanup.StopAsync(
                    () => provider.StopActionAsync(created.ProviderActionId, CancellationToken.None),
                    app.Logger,
                    "Failed to clean up JIT-extension diagnostic action {ProviderActionId}.",
                    created.ProviderActionId);
            }
        }
    });

    app.MapPost("/api/dev/parking-provider/actions/test-active-shortening", async (
        DevActiveShorteningRequest request,
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
            return Results.BadRequest(new { error = "LicensePlate is verplicht." });
        if (request.InitialDurationMinutes is < 3 or > 15)
            return Results.BadRequest(new { error = "InitialDurationMinutes moet tussen 3 en 15 liggen." });
        if (request.ShortenedDurationMinutes is < 1 or > 14 ||
            request.ShortenedDurationMinutes >= request.InitialDurationMinutes)
            return Results.BadRequest(new { error = "ShortenedDurationMinutes moet minimaal 1 zijn en kleiner dan InitialDurationMinutes." });

        var product = await provider.GetProductAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(product.Location))
            return Results.Problem("2Park-locatie kon niet worden bepaald.", statusCode: StatusCodes.Status503ServiceUnavailable);

        Parkeren.Application.ParkingProvider.ProviderParkingAction? created = null;
        try
        {
            var start = DateTimeOffset.UtcNow;
            var originalEnd = start.AddMinutes(request.InitialDurationMinutes);
            var shortenedEnd = start.AddMinutes(request.ShortenedDurationMinutes);

            created = await provider.StartActionAsync(
                new ProviderParkingActionRequest(
                    request.LicensePlate,
                    start,
                    originalEnd,
                    product.Location),
                cancellationToken);

            var before = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

            var changed = await provider.ExtendActionAsync(
                created.ProviderActionId,
                shortenedEnd,
                cancellationToken);

            var after = (await provider.GetActionsAsync(cancellationToken))
                .SingleOrDefault(x => x.ProviderActionId == created.ProviderActionId);

            return Results.Ok(new
            {
                requested = new { start, originalEnd, shortenedEnd },
                created,
                before,
                changed,
                after,
                shorteningApplied = after is not null &&
                    (after.End - shortenedEnd).Duration() < TimeSpan.FromMilliseconds(1)
            });
        }
        finally
        {
            if (created is not null)
            {
                await DiagnosticActionCleanup.StopAsync(
                    () => provider.StopActionAsync(created.ProviderActionId, CancellationToken.None),
                    app.Logger,
                    "Failed to clean up active-shortening diagnostic action {ProviderActionId}.",
                    created.ProviderActionId);
            }
        }
    });

    app.MapPost("/api/dev/parking-provider/actions/{providerActionId}/stop", async (
        string providerActionId,
        IParkingProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(providerActionId))
            return Results.BadRequest(new { error = "ProviderActionId is verplicht." });

        await provider.StopActionAsync(providerActionId, cancellationToken);
        var actions = await provider.GetActionsAsync(cancellationToken);
        return Results.Ok(new { stoppedProviderActionId = providerActionId, actions });
    });
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var isHtml = context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
        var isServiceWorker = context.Request.Path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase);
        if (isHtml || isServiceWorker)
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }

        return Task.CompletedTask;
    });

    await next();
});

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

app.MapGet("/api/admin/budgets", async (
    Guid? productId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var periods = productId.HasValue
        ? await administration.GetBudgetPeriodsForProductAsync(
            authenticated.User.Id,
            productId.Value,
            cancellationToken)
        : await administration.GetBudgetPeriodsAsync(
            authenticated.User.Id,
            cancellationToken);

    return Results.Ok(periods);
});

app.MapPost("/api/admin/budgets", async (
    AdminBudgetPeriodCreateRequest request,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var result = request.ProviderProductId.HasValue
        ? await administration.CreateBudgetPeriodForProductAsync(
            authenticated.User.Id,
            request.ProviderProductId.Value,
            request.ValidFrom,
            request.ValidUntil,
            request.MaximumPaidDurationMinutes,
            cancellationToken)
        : await administration.CreateBudgetPeriodAsync(
            authenticated.User.Id,
            request.ValidFrom,
            request.ValidUntil,
            request.MaximumPaidDurationMinutes,
            cancellationToken);

    return result.Outcome switch
    {
        AdminBudgetPeriodCreateOutcome.Created => Results.Ok(result),
        AdminBudgetPeriodCreateOutcome.Overlap => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };
});

app.MapGet("/api/admin/budgets/usage", async (
    Guid? periodId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var result = await administration.GetBudgetUsageAsync(
        authenticated.User.Id,
        periodId,
        DateTimeOffset.UtcNow,
        cancellationToken);

    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/admin/tariffs", async (
    Guid? productId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var tariffs = productId.HasValue
        ? await administration.GetParkingTariffsForProductAsync(
            authenticated.User.Id,
            productId.Value,
            cancellationToken)
        : await administration.GetParkingTariffsAsync(
            authenticated.User.Id,
            cancellationToken);

    return Results.Ok(tariffs);
});

app.MapPost("/api/admin/tariffs", async (
    AdminParkingTariffCreateRequest request,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var result = request.ProviderProductId.HasValue
        ? await administration.CreateParkingTariffForProductAsync(
            authenticated.User.Id,
            request.ProviderProductId.Value,
            request.ValidFrom,
            request.ValidUntil,
            request.Rate,
            request.Unit,
            cancellationToken)
        : await administration.CreateParkingTariffAsync(
            authenticated.User.Id,
            request.ValidFrom,
            request.ValidUntil,
            request.Rate,
            request.Unit,
            cancellationToken);

    return result.Outcome switch
    {
        AdminParkingTariffCreateOutcome.Created => Results.Ok(result),
        AdminParkingTariffCreateOutcome.Overlap => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };
});

app.MapGet("/api/admin/analysis/usage", async (
    DateTimeOffset from,
    DateTimeOffset to,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    if (to <= from) return Results.BadRequest(new { error = "to moet na from liggen." });

    return Results.Ok(await administration.GetUsageAnalysisAsync(
        authenticated.User.Id,
        from,
        to,
        cancellationToken));
});

app.MapGet("/api/admin/costs", async (
    DateTimeOffset from,
    DateTimeOffset to,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    if (to <= from) return Results.BadRequest(new { error = "to moet na from liggen." });

    return Results.Ok(await administration.GetCostReportAsync(
        authenticated.User.Id,
        from,
        to,
        cancellationToken));
});

app.MapGet("/api/admin/parking-rules", async (
    Guid? productId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var rules = productId.HasValue
        ? await administration.GetParkingRuleSetsForProductAsync(
            authenticated.User.Id,
            productId.Value,
            cancellationToken)
        : await administration.GetParkingRuleSetsAsync(
            authenticated.User.Id,
            cancellationToken);

    return Results.Ok(rules);
});

app.MapPost("/api/admin/parking-rules", async (
    AdminParkingRuleSetCreateRequest request,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var result = request.ProviderProductId.HasValue
        ? await administration.CreateParkingRuleSetVersionForProductAsync(
            authenticated.User.Id,
            request.ProviderProductId.Value,
            request.ValidFrom,
            request.MaxProviderActionDurationMinutes,
            request.Continuation,
            request.PublicHolidaysAreFree,
            request.PaidWindows,
            request.CalendarExceptions,
            DateTimeOffset.UtcNow,
            cancellationToken)
        : await administration.CreateParkingRuleSetVersionAsync(
            authenticated.User.Id,
            request.ValidFrom,
            request.MaxProviderActionDurationMinutes,
            request.Continuation,
            request.PublicHolidaysAreFree,
            request.PaidWindows,
            request.CalendarExceptions,
            DateTimeOffset.UtcNow,
            cancellationToken);

    return result.Outcome switch
    {
        AdminParkingRuleSetCreateOutcome.Created => Results.Ok(result),
        AdminParkingRuleSetCreateOutcome.SequenceConflict => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };
});

app.MapGet("/api/admin/system/settings", async (
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    return Results.Ok(await administration.GetSystemSettingsAsync(authenticated.User.Id, cancellationToken));
});

app.MapGet("/api/admin/system/diagnostics", async (
    IAdminSystemDiagnosticsService diagnostics,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    return Results.Ok(await diagnostics.GetAsync(cancellationToken));
});

app.MapGet("/api/admin/system/audit", async (
    Guid? actorUserId,
    string? action,
    string? targetType,
    string? targetId,
    DateTimeOffset? from,
    DateTimeOffset? to,
    int? limit,
    IAdminAuditQueryService audit,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    if (from.HasValue && to.HasValue && to.Value < from.Value)
        return Results.BadRequest(new { error = "Tot-datum mag niet voor van-datum liggen." });

    return Results.Ok(await audit.GetEventsAsync(
        authenticated.User.Id,
        new AdminAuditQuery(actorUserId, action, targetType, targetId, from, to, limit ?? 100),
        cancellationToken));
});

app.MapPut("/api/admin/system/default-policy", async (
    AdminDefaultPolicyUpdateRequest request,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var result = await administration.SetDefaultParkingPolicyAsync(
        authenticated.User.Id,
        request.MaxPaidParkingDurationMinutes,
        request.MaxVisitElapsedDurationMinutes,
        request.AllowVisitExtension,
        request.AllowOpenEndedVisits,
        request.MaxConcurrentVisits,
        cancellationToken);

    return result.Outcome switch
    {
        AdminDefaultPolicyUpdateOutcome.Updated => Results.Ok(result),
        AdminDefaultPolicyUpdateOutcome.ActiveVisitConflict => Results.Conflict(result),
        _ => Results.BadRequest(result)
    };
});

app.MapPut("/api/admin/system/warnings", async (
    AdminWarningSettingsUpdateRequest request,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();

    var result = await administration.SetWarningSettingsAsync(
        authenticated.User.Id,
        request.LongVisitWarningAfterMinutes,
        request.NotifyAdminOnLongVisit,
        request.LongVisitReminderIntervalMinutes,
        request.BudgetWarningThresholdPercentages,
        cancellationToken);

    return result.Outcome == AdminWarningSettingsUpdateOutcome.Updated
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapGet("/api/admin/dashboard", async (
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return Results.Ok(await administration.GetDashboardAsync(
        authenticated.User.Id,
        DateTimeOffset.UtcNow,
        cancellationToken));
});

app.MapGet("/api/admin/users/{userId:guid}/detail", async (
    Guid userId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    var detail = await administration.GetUserDetailAsync(authenticated.User.Id, userId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapPut("/api/admin/users/{userId:guid}/policy", async (
    Guid userId,
    AdminUserPolicyUpdateRequest request,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    var result = await administration.SetUserPolicyAsync(
        authenticated.User.Id,
        userId,
        request.MaxPaidParkingDurationMode,
        request.MaxPaidParkingDurationMinutes,
        request.MaxVisitElapsedDurationMode,
        request.MaxVisitElapsedDurationMinutes,
        request.AllowVisitExtension,
        request.AllowOpenEndedVisits,
        request.MaxConcurrentVisits,
        cancellationToken);

    return result.Outcome switch
    {
        AdminUserPolicyUpdateOutcome.Updated => Results.Ok(result),
        AdminUserPolicyUpdateOutcome.NotFound => Results.NotFound(),
        AdminUserPolicyUpdateOutcome.ActiveVisitConflict => Results.Conflict(result),
        _ => Results.BadRequest(new { error = "Ongeldige gebruikerspolicy." })
    };
});

app.MapGet("/api/admin/users/{userId:guid}/parking-policy", async (
    Guid userId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    var policy = await administration.GetUserParkingPolicyAsync(
        authenticated.User.Id,
        userId,
        DateTimeOffset.UtcNow,
        cancellationToken);

    return policy is null ? Results.NotFound() : Results.Ok(policy);
});

app.MapGet("/api/admin/provider/status", async (
    IAdminProviderStatusService providerStatus,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return Results.Ok(await providerStatus.GetStatusAsync(cancellationToken));
});

app.MapPost("/api/admin/provider-history/sync", async (
    ProviderHistorySyncStartRequest request,
    ProviderHistorySyncRunStarter starter,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (string.IsNullOrWhiteSpace(request.ProviderProductId) || request.ProviderProductId.Trim().Length > 100)
        return Results.BadRequest(new { error = "Geldig providerproduct is vereist." });

    var run = await starter.TryStartAsync(
        request.ProviderProductId, Parkeren.Domain.ParkingProvider.ProviderHistorySyncRunMode.Manual,
        cancellationToken);
    return run is null
        ? Results.Conflict(new { error = "Er loopt al een synchronisatie voor dit product." })
        : Results.Accepted($"/api/admin/provider-history/sync-status?providerProductId={Uri.EscapeDataString(run.ProviderProductId)}",
            new { run.Id, run.ProviderProductId, run.Status });
});

app.MapPost("/api/admin/provider-history/sync/{runId:guid}/cancel", async (
    Guid runId,
    ProviderHistorySyncRunCanceller canceller,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var cancelled = await canceller.TryCancelAsync(runId, cancellationToken);
    if (cancelled is null)
        return Results.NotFound(new { error = "Synchronisatierun niet gevonden." });
    if (!cancelled.Value)
        return Results.Conflict(new { error = "De synchronisatierun is al afgerond of wordt momenteel uitgevoerd." });

    return Results.Ok(new { runId, status = "Cancelled" });
});

app.MapGet("/api/admin/provider-history/sync-status", async (
    string? providerProductId,
    ParkerenDbContext db,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var states = await db.ProviderHistorySyncStates.AsNoTracking()
        .Where(x => providerProductId == null || x.ProviderProductId == providerProductId)
        .OrderBy(x => x.ProviderProductId)
        .ToListAsync(cancellationToken);
    var runs = await db.ProviderHistorySyncRuns.AsNoTracking()
        .Where(x => providerProductId == null || x.ProviderProductId == providerProductId)
        .OrderByDescending(x => x.StartedAt)
        .ThenByDescending(x => x.Id)
        .Take(25)
        .ToListAsync(cancellationToken);

    return Results.Ok(new { states, runs });
});

app.MapGet("/api/admin/provider/actions", async (
    int? page,
    int? pageSize,
    string? search,
    string? providerProductId,
    ProviderActionState? state,
    ProviderActionOrigin? origin,
    Guid? assignedUserId,
    DateTimeOffset? from,
    DateTimeOffset? until,
    bool? oldestFirst,
    bool? hasOpenDiscrepancy,
    IAdminProviderActionHistoryQuery history,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (page is < 1 || pageSize is < 1 or > 100)
        return Results.BadRequest("Page must be positive and page size between 1 and 100.");
    if (from.HasValue && until.HasValue && from >= until)
        return Results.BadRequest("From must be earlier than Until.");

    return Results.Ok(await history.GetAsync(
        new AdminProviderActionHistoryFilter(page ?? 1, pageSize ?? 25, search,
            providerProductId, state, origin, assignedUserId, from, until, oldestFirst ?? false, hasOpenDiscrepancy), cancellationToken));
});

app.MapGet("/api/admin/provider/actions/{actionId:guid}/assignment-history", async (
    Guid actionId,
    ParkerenDbContext db,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!await db.ProviderParkingActions.AnyAsync(x => x.Id == actionId, cancellationToken))
        return Results.NotFound();

    var events = await db.AdminAuditEvents.AsNoTracking()
        .Where(x => x.TargetType == ProviderActionAssignmentAudit.TargetName &&
                    x.TargetId == actionId.ToString("D") &&
                    x.Action == ProviderActionAssignmentAudit.ActionName)
        .OrderByDescending(x => x.CreatedAt)
        .ThenByDescending(x => x.Id)
        .Select(x => new
        {
            x.Id, x.CreatedAt, x.ActorUserId,
            ActorUsername = db.Users.Where(u => u.Id == x.ActorUserId)
                .Select(u => u.Username).FirstOrDefault(),
            x.ContextJson
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(events);
});

app.MapPut("/api/admin/provider/actions/{actionId:guid}/assignment", async (
    Guid actionId,
    ProviderActionAssignmentRequest request,
    ProviderHistoryAssignmentService assignment,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    try
    {
        var changed = await assignment.AssignAsync(
            actionId, authenticated.User.Id, request.UserId, cancellationToken);
        return Results.Ok(new { changed });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/admin/provider/discrepancies", async (
    bool includeResolved,
    IProviderDiscrepancyService discrepancyService,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return Results.Ok(await discrepancyService.GetAsync(includeResolved, cancellationToken));
});

app.MapGet("/api/admin/provider/products", async (
    IProviderProductCatalogService productCatalog,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return Results.Ok(await productCatalog.GetProductsAsync(cancellationToken));
});

app.MapPost("/api/admin/provider/products/sync", async (
    IProviderProductCatalogService productCatalog,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    try
    {
        return Results.Ok(await productCatalog.SynchronizeForAdminAsync(authenticated.User.Id, cancellationToken));
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Problem(
            exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPut("/api/admin/provider/products/{productId:guid}/default", async (
    Guid productId,
    IProviderProductCatalogService productCatalog,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    return await productCatalog.SetDefaultForAdminAsync(authenticated.User.Id, productId, cancellationToken)
        ? Results.NoContent()
        : Results.BadRequest(new { error = "Alleen een beschikbaar providerproduct kan default worden gemaakt." });
});

app.MapGet("/api/admin/visits", async (
    Guid? userId,
    string? licensePlate,
    DateTimeOffset? from,
    DateTimeOffset? to,
    VisitStatus? status,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();
    if (from.HasValue && to.HasValue && from.Value > to.Value)
        return Results.BadRequest(new { error = "Van-datum mag niet na tot-datum liggen." });

    var visits = await administration.GetVisitsAsync(
        authenticated.User.Id,
        userId,
        licensePlate,
        from,
        to,
        status,
        DateTimeOffset.UtcNow,
        cancellationToken);

    return Results.Ok(visits);
});

app.MapGet("/api/admin/visits/{visitId:guid}", async (
    Guid visitId,
    IAdministrationService administration,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin)
        return Results.Forbid();

    var visit = await administration.GetVisitDetailAsync(
        authenticated.User.Id,
        visitId,
        DateTimeOffset.UtcNow,
        cancellationToken);

    return visit is null ? Results.NotFound() : Results.Ok(visit);
});

app.MapGet("/api/visits/policy", async (
    IStartVisitOperationalContextResolver operationalContextResolver,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var now = DateTimeOffset.UtcNow;
    var operationalContext = await operationalContextResolver.ResolveAsync(
        authenticated.User.Id,
        now,
        null,
        cancellationToken);
    if (operationalContext is null)
        return Results.Problem("Parkeerbeleid is niet beschikbaar.", statusCode: StatusCodes.Status503ServiceUnavailable);

    return Results.Ok(new
    {
        maxPaidParkingDurationMinutes = operationalContext.Policy.MaxPaidParkingDuration is null
            ? (int?)null
            : (int)operationalContext.Policy.MaxPaidParkingDuration.Value.TotalMinutes,
        maxVisitElapsedDurationMinutes = operationalContext.Policy.MaxVisitElapsedDuration is null
            ? (int?)null
            : (int)operationalContext.Policy.MaxVisitElapsedDuration.Value.TotalMinutes,
        operationalContext.Policy.AllowVisitExtension,
        operationalContext.Policy.AllowOpenEndedVisits
    });
});

app.MapPost("/api/visits/start-preview", async (
    StartVisitPreviewRequest request,
    IStartVisitRequestResolver requestResolver,
    IStartVisitOperationalContextResolver operationalContextResolver,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    if (request.VehicleId == Guid.Empty)
        return Results.BadRequest(new { error = "VehicleId is verplicht." });

    var ownerUserId = request.OwnerUserId ?? authenticated.User.Id;
    var startAt = DateTimeOffset.UtcNow;

    var requestContext = await requestResolver.ResolveAsync(
        authenticated.User.Id,
        ownerUserId,
        request.VehicleId,
        cancellationToken);
    if (requestContext is null)
        return Results.BadRequest(new { error = "Visit-context kon niet worden bepaald." });

    try
    {
        StartVisitAuthorization.Validate(
            requestContext.StartContext.Actor,
            requestContext.StartContext.Owner,
            requestContext.StartContext.Vehicle);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }

    if (requestContext.ProviderContext?.ProductId is not Guid providerProductId)
        return Results.Problem(
            "Er is geen beschikbaar default parkeerproduct geconfigureerd.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var operationalContext = await operationalContextResolver.ResolveForProductAsync(
        ownerUserId,
        providerProductId,
        startAt,
        request.DesiredEndAt,
        cancellationToken);
    if (operationalContext is null)
        return Results.Problem(
            "Parkeerbeleid of parkeerregels zijn niet beschikbaar.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    StartVisitPolicyAssessment assessment;
    try
    {
        assessment = StartVisitPolicyAssessor.Assess(
            startAt,
            request.DesiredEndAt,
            operationalContext.Policy,
            operationalContext.RuleSets);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }

    return Results.Ok(new
    {
        assessment.IsAllowed,
        paidDurationMinutes = assessment.PaidDuration is null
            ? (int?)null
            : (int)Math.Ceiling(assessment.PaidDuration.Value.TotalMinutes),
        elapsedDurationMinutes = assessment.ElapsedDuration is null
            ? (int?)null
            : (int)Math.Ceiling(assessment.ElapsedDuration.Value.TotalMinutes),
        rejectionReason = assessment.RejectionReason?.ToString()
    });
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
    if (requestContext.ProviderContext?.ProductId is not Guid providerProductId ||
        string.IsNullOrWhiteSpace(requestContext.ProviderContext.ProviderProductId))
        return Results.Problem(
            "Er is geen beschikbaar default parkeerproduct geconfigureerd.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var operationalContext = await operationalContextResolver.ResolveForProductAsync(
        ownerUserId,
        providerProductId,
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

app.MapGet("/api/visits/capacity", async (
    ParkerenDbContext dbContext,
    IStartVisitOperationalContextResolver operationalContextResolver,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var operationalContext = await operationalContextResolver.ResolveAsync(
        authenticated.User.Id,
        DateTimeOffset.UtcNow,
        null,
        cancellationToken);

    if (operationalContext is null)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    var used = await dbContext.Visits
        .AsNoTracking()
        .CountAsync(x => x.Status == VisitStatus.Starting || x.Status == VisitStatus.Active || x.Status == VisitStatus.Stopping, cancellationToken);

    return Results.Ok(new { used, total = operationalContext.MaxConcurrentVisits });
});

app.MapGet("/api/notifications", async (
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var notifications = await dbContext.Notifications
        .AsNoTracking()
        .Where(x => x.RecipientUserId == authenticated.User.Id)
        .OrderByDescending(x => x.CreatedAt)
        .Select(x => new
        {
            x.Id,
            x.Type,
            x.VisitId,
            x.Payload,
            x.CreatedAt,
            x.ReadAt,
            isRead = x.ReadAt != null
        })
        .Take(100)
        .ToListAsync(cancellationToken);

    return Results.Ok(notifications);
});

app.MapGet("/api/notifications/unread-count", async (
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var count = await dbContext.Notifications
        .CountAsync(x => x.RecipientUserId == authenticated.User.Id && x.ReadAt == null, cancellationToken);

    return Results.Ok(new { count });
});

app.MapPost("/api/notifications/{notificationId:guid}/read", async (
    Guid notificationId,
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var notification = await dbContext.Notifications
        .SingleOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == authenticated.User.Id, cancellationToken);
    if (notification is null)
        return Results.NotFound();

    notification.MarkRead(DateTimeOffset.UtcNow);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/notifications/read-all", async (
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var now = DateTimeOffset.UtcNow;
    var notifications = await dbContext.Notifications
        .Where(x => x.RecipientUserId == authenticated.User.Id && x.ReadAt == null)
        .ToListAsync(cancellationToken);

    foreach (var notification in notifications)
        notification.MarkRead(now);

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapDelete("/api/notifications/{notificationId:guid}", async (
    Guid notificationId,
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var notification = await dbContext.Notifications
        .SingleOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == authenticated.User.Id, cancellationToken);
    if (notification is null)
        return Results.NotFound();

    dbContext.Notifications.Remove(notification);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/notifications/push-public-key", async (
    IAuthenticationService authentication,
    HttpContext context,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var publicKey = configuration["WebPush:PublicKey"];
    return string.IsNullOrWhiteSpace(publicKey)
        ? Results.Problem("Web Push is niet geconfigureerd.", statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { publicKey });
});

app.MapPost("/api/notifications/push-subscriptions", async (
    PushSubscriptionRequest request,
    PushSubscriptionService pushSubscriptions,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Endpoint) ||
        string.IsNullOrWhiteSpace(request.P256dh) ||
        string.IsNullOrWhiteSpace(request.Auth))
        return Results.BadRequest(new { error = "Een geldige push-subscription is verplicht." });

    var result = await pushSubscriptions.RegisterAsync(
        authenticated.User.Id,
        request.Endpoint,
        request.P256dh,
        request.Auth,
        cancellationToken);

    return result == PushSubscriptionRegistrationResult.EndpointOwnedByAnotherUser
        ? Results.Conflict(new { error = "Deze push-subscription is al gekoppeld." })
        : Results.NoContent();
});

app.MapDelete("/api/notifications/push-subscriptions", async (
    [FromBody] PushSubscriptionDeleteRequest request,
    PushSubscriptionService pushSubscriptions,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Endpoint))
        return Results.BadRequest(new { error = "Push endpoint is verplicht." });

    await pushSubscriptions.RemoveAsync(
        authenticated.User.Id,
        request.Endpoint,
        cancellationToken);
    return Results.NoContent();
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

app.MapGet("/api/visits/recent", async (
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var visits = await dbContext.Visits
        .AsNoTracking()
        .Where(x => x.UserId == authenticated.User.Id)
        .Where(x => x.Status == VisitStatus.Completed || x.Status == VisitStatus.Cancelled)
        .OrderByDescending(x => x.ActualEndAt ?? x.StartAt)
        .Take(5)
        .ToListAsync(cancellationToken);

    return Results.Ok(visits);
});

app.MapGet("/api/visits/history", async (
    ParkerenDbContext dbContext,
    IAuthenticationService authentication,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null)
        return Results.Unauthorized();

    var visits = await dbContext.Visits
        .AsNoTracking()
        .Where(x => x.UserId == authenticated.User.Id)
        .Where(x => x.Status == VisitStatus.Completed || x.Status == VisitStatus.Cancelled)
        .OrderByDescending(x => x.ActualEndAt ?? x.StartAt)
        .Take(100)
        .ToListAsync(cancellationToken);

    return Results.Ok(visits);
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
    try
    {
        return await administration.SetUserActiveAsync(authenticated.User.Id, userId, request.IsActive, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapPut("/api/admin/users/{userId:guid}/archive", async (
    Guid userId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    try
    {
        return await administration.ArchiveUserAsync(authenticated.User.Id, userId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapDelete("/api/admin/users/{userId:guid}", async (
    Guid userId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    try
    {
        return await administration.DeleteUserAsync(authenticated.User.Id, userId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapPut("/api/admin/users/{userId:guid}/policy/max-concurrent-visits", async (
    Guid userId, SetMaxConcurrentVisitsRequest request, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return await administration.SetUserMaxConcurrentVisitsAsync(authenticated.User.Id, userId, request.MaxConcurrentVisits, cancellationToken)
        ? Results.NoContent()
        : Results.BadRequest();
});

app.MapGet("/api/admin/parking-settings/max-concurrent-visits", async (
    IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    return Results.Ok(new { maxConcurrentVisits = await administration.GetGlobalMaxConcurrentVisitsAsync(authenticated.User.Id, cancellationToken) });
});

app.MapPut("/api/admin/parking-settings/max-concurrent-visits", async (
    SetMaxConcurrentVisitsRequest request, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    if (request.MaxConcurrentVisits is not > 0) return Results.BadRequest();
    return await administration.SetGlobalMaxConcurrentVisitsAsync(authenticated.User.Id, request.MaxConcurrentVisits.Value, cancellationToken)
        ? Results.NoContent()
        : Results.Conflict();
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
    try
    {
        return await administration.SetVehicleActiveAsync(authenticated.User.Id, vehicleId, request.IsActive, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapPut("/api/admin/vehicles/{vehicleId:guid}/archive", async (
    Guid vehicleId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    try
    {
        return await administration.ArchiveVehicleAsync(authenticated.User.Id, vehicleId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

app.MapDelete("/api/admin/vehicles/{vehicleId:guid}", async (
    Guid vehicleId, IAdministrationService administration, IAuthenticationService authentication, HttpContext context, CancellationToken cancellationToken) =>
{
    var authenticated = await GetAuthenticatedAsync(authentication, context, cancellationToken);
    if (authenticated.User is null) return Results.Unauthorized();
    if (authenticated.User.Role != UserRole.Admin) return Results.Forbid();
    try
    {
        return await administration.DeleteVehicleAsync(authenticated.User.Id, vehicleId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
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

public sealed record DevStartProviderActionRequest(string LicensePlate, int DurationMinutes, int StartInMinutes = 0);
public sealed record DevAdjacentProviderActionsRequest(string LicensePlate, int FirstDurationMinutes = 5, int SecondDurationMinutes = 5);
public sealed record DevActiveShorteningRequest(string LicensePlate, int InitialDurationMinutes = 5, int ShortenedDurationMinutes = 2);
public sealed record DevActiveExtensionRequest(string LicensePlate, int InitialDurationMinutes = 2, int ExtendedDurationMinutes = 5);
public sealed record DevJitExtensionRequest(string LicensePlate, int InitialDurationMinutes = 3, int ExtendBeforeEndSeconds = 60, int ExtensionMinutes = 3);
public sealed record LoginRequest(string Username, string Pin);
public sealed record ChangePinRequest(string CurrentPin, string NewPin);
public sealed record ResetPinRequest(string NewPin);
public sealed record CreateUserRequest(string Username, string Pin, UserRole Role);
public sealed record CreateVehicleRequest(string LicensePlate, string? DisplayName);
public sealed record SetActiveRequest(bool IsActive);
public sealed record SetMaxConcurrentVisitsRequest(int? MaxConcurrentVisits);
public sealed record AdminBudgetPeriodCreateRequest(
    Guid? ProviderProductId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    int MaximumPaidDurationMinutes);
public sealed record AdminParkingTariffCreateRequest(
    Guid? ProviderProductId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    decimal Rate,
    ParkingTariffUnit Unit);
public sealed record AdminParkingRuleSetCreateRequest(
    Guid? ProviderProductId,
    DateTimeOffset ValidFrom,
    int MaxProviderActionDurationMinutes,
    ProviderCoverageContinuation Continuation,
    bool PublicHolidaysAreFree,
    IReadOnlyList<AdminPaidWindowInput> PaidWindows,
    IReadOnlyList<AdminCalendarExceptionInput> CalendarExceptions);
public sealed record AdminDefaultPolicyUpdateRequest(
    int? MaxPaidParkingDurationMinutes,
    int? MaxVisitElapsedDurationMinutes,
    bool AllowVisitExtension,
    bool AllowOpenEndedVisits,
    int MaxConcurrentVisits);
public sealed record AdminWarningSettingsUpdateRequest(
    int? LongVisitWarningAfterMinutes,
    bool NotifyAdminOnLongVisit,
    int? LongVisitReminderIntervalMinutes,
    IReadOnlyList<int> BudgetWarningThresholdPercentages);
public sealed record AdminUserPolicyUpdateRequest(
    PolicyDurationOverrideMode MaxPaidParkingDurationMode,
    int? MaxPaidParkingDurationMinutes,
    PolicyDurationOverrideMode MaxVisitElapsedDurationMode,
    int? MaxVisitElapsedDurationMinutes,
    bool? AllowVisitExtension,
    bool? AllowOpenEndedVisits,
    int? MaxConcurrentVisits);
public sealed record StartVisitPreviewRequest(Guid VehicleId, Guid? OwnerUserId, DateTimeOffset? DesiredEndAt);
public sealed record StartVisitRequest(Guid OperationId, Guid VehicleId, Guid? OwnerUserId, DateTimeOffset? DesiredEndAt);
public sealed record StopVisitRequest(Guid OperationId);
public sealed record ChangeVisitEndTimeRequest(Guid OperationId, DateTimeOffset? DesiredEndAt);

public partial class Program;

public sealed record ProviderActionAssignmentRequest(Guid? UserId);

public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
public sealed record PushSubscriptionDeleteRequest(string Endpoint);
public sealed record ProviderHistorySyncStartRequest(string ProviderProductId);
