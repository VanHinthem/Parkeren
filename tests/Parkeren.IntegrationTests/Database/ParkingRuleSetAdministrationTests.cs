using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ParkingRuleSetAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Creating_future_version_closes_previous_version_and_persists_children()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);

        Guid previousId;
        DateTimeOffset? previousValidUntil;
        DateTimeOffset validFrom;

        await using (var seed = fixture.CreateDbContext())
        {
            var latest = await seed.ParkingRuleSets
                .OrderByDescending(x => x.ValidFrom)
                .FirstAsync(ct);
            previousId = latest.Id;
            previousValidUntil = latest.ValidUntil;

            var candidate = DateTimeOffset.UtcNow.AddDays(30);
            validFrom = latest.ValidFrom >= candidate
                ? latest.ValidFrom.AddDays(1)
                : candidate;

            seed.Users.Add(admin);
            await seed.SaveChangesAsync(ct);
        }

        Guid? createdId = null;

        try
        {
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

            var result = await administration.CreateParkingRuleSetVersionAsync(
                admin.Id,
                validFrom,
                maxProviderActionDurationMinutes: 180,
                continuation: ProviderCoverageContinuation.ExtendAction,
                publicHolidaysAreFree: false,
                paidWindows:
                [
                    new AdminPaidWindowInput(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)),
                    new AdminPaidWindowInput(DayOfWeek.Monday, new TimeOnly(13, 0), new TimeOnly(18, 0))
                ],
                calendarExceptions:
                [
                    new AdminCalendarExceptionInput(DateOnly.FromDateTime(validFrom.Date.AddDays(2)), false)
                ],
                now: DateTimeOffset.UtcNow,
                ct);

            Assert.Equal(AdminParkingRuleSetCreateOutcome.Created, result.Outcome);
            Assert.NotNull(result.Version);
            createdId = result.Version.Id;

            await using var verify = fixture.CreateDbContext();
            var previous = await verify.ParkingRuleSets.SingleAsync(x => x.Id == previousId, ct);
            var created = await verify.ParkingRuleSets
                .Include(x => x.PaidWindows)
                .Include(x => x.CalendarExceptions)
                .SingleAsync(x => x.Id == createdId.Value, ct);

            Assert.Equal(created.ValidFrom, previous.ValidUntil);
            Assert.InRange(
                (created.ValidFrom - validFrom).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
            Assert.Null(created.ValidUntil);
            Assert.Equal(TimeSpan.FromHours(3), created.MaxProviderActionDuration);
            Assert.Equal(ProviderCoverageContinuation.ExtendAction, created.Continuation);
            Assert.False(created.PublicHolidaysAreFree);
            Assert.Equal(2, created.PaidWindows.Count);
            Assert.Single(created.CalendarExceptions);

            var allRules = await verify.ParkingRuleSets.AsNoTracking().ToListAsync(ct);
            ParkingRuleSetResolver.ValidateNoOverlap(allRules);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();

            if (createdId.HasValue)
            {
                await cleanup.PaidWindows
                    .Where(x => x.ParkingRuleSetId == createdId.Value)
                    .ExecuteDeleteAsync(ct);
                await cleanup.ParkingCalendarExceptions
                    .Where(x => x.ParkingRuleSetId == createdId.Value)
                    .ExecuteDeleteAsync(ct);
                await cleanup.ParkingRuleSets
                    .Where(x => x.Id == createdId.Value)
                    .ExecuteDeleteAsync(ct);
            }

            await cleanup.ParkingRuleSets
                .Where(x => x.Id == previousId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ValidUntil, previousValidUntil), ct);

            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
        }
    }

    [Fact]
    public async Task New_rule_version_must_start_in_future()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"admin-{suffix}", $"ADMIN-{suffix}", "hash", UserRole.Admin);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.Add(admin);
            await seed.SaveChangesAsync(ct);
        }

        try
        {
            var services = CreateServices();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
            var now = DateTimeOffset.UtcNow;

            var result = await administration.CreateParkingRuleSetVersionAsync(
                admin.Id,
                now,
                maxProviderActionDurationMinutes: 240,
                continuation: ProviderCoverageContinuation.StartNewAction,
                publicHolidaysAreFree: true,
                paidWindows: Array.Empty<AdminPaidWindowInput>(),
                calendarExceptions: Array.Empty<AdminCalendarExceptionInput>(),
                now,
                ct);

            Assert.Equal(AdminParkingRuleSetCreateOutcome.MustBeFuture, result.Outcome);
            Assert.Null(result.Version);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Users.Where(x => x.Id == admin.Id).ExecuteDeleteAsync(ct);
        }
    }

    private ServiceCollection CreateServices()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Parkeren"] = fixture.ConnectionString
        });
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services;
    }
}
