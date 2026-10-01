using Microsoft.EntityFrameworkCore;
using Parkeren.Infrastructure.Persistence;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Testcontainers.PostgreSql;

namespace Parkeren.IntegrationTests.Database;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("parkeren_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.ParkingSystemSettings.Add(new ParkingSystemSettings(Guid.NewGuid(), 5));
        dbContext.DefaultParkingPolicies.Add(new DefaultParkingPolicy(
            Guid.NewGuid(),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(8),
            allowVisitExtension: true,
            allowOpenEndedVisits: false));

        var paidWindows = Enumerable.Range((int)DayOfWeek.Monday, 6)
            .Select(day => new PaidWindow(
                (DayOfWeek)day,
                new TimeOnly(9, 0),
                new TimeOnly(20, 0)))
            .ToArray();

        dbContext.ParkingRuleSets.Add(new ParkingRuleSet(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            validUntil: null,
            TimeSpan.FromHours(4),
            paidWindows,
            publicHolidaysAreFree: true,
            continuation: ProviderCoverageContinuation.StartNewAction));

        await dbContext.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }

    public ParkerenDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ParkerenDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ParkerenDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}
