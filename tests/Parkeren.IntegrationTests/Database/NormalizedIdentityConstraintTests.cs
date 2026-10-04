using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class NormalizedIdentityConstraintTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Database_rejects_duplicate_normalized_usernames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var normalizedUsername = $"USER-{suffix}".ToUpperInvariant();
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.AddRange(
            new User(Guid.NewGuid(), $"user-a-{suffix}", normalizedUsername, "hash", UserRole.Visitor),
            new User(Guid.NewGuid(), $"user-b-{suffix}", normalizedUsername, "hash", UserRole.Visitor));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            async () => await dbContext.SaveChangesAsync(cancellationToken));

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("IX_users_NormalizedUsername", postgresException.ConstraintName);
    }

    [Fact]
    public async Task Database_rejects_duplicate_normalized_license_plates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var normalizedPlate = $"UV{suffix}";
        var formattedPlate = $"UV-{suffix[..4]}-{suffix[4..]}";
        await using var dbContext = fixture.CreateDbContext();
        dbContext.Vehicles.AddRange(
            new Vehicle(Guid.NewGuid(), formattedPlate, normalizedPlate, null),
            new Vehicle(Guid.NewGuid(), normalizedPlate, normalizedPlate, null));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            async () => await dbContext.SaveChangesAsync(cancellationToken));

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal("IX_vehicles_NormalizedLicensePlate", postgresException.ConstraintName);
    }
}