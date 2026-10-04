using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;

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

    [Fact]
    public async Task Database_rejects_duplicate_user_vehicle_assignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User(Guid.NewGuid(), $"assignment-user-{suffix}", $"ASSIGNMENT-USER-{suffix}", "hash", UserRole.Visitor);
        var plate = $"ZX{suffix[..6].ToUpperInvariant()}";
        var vehicle = new Vehicle(Guid.NewGuid(), plate, plate, null);

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.Users.Add(user);
                seed.Vehicles.Add(vehicle);
                seed.UserVehicles.Add(new UserVehicle(user.Id, vehicle.Id));
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var duplicate = fixture.CreateDbContext();
            duplicate.UserVehicles.Add(new UserVehicle(user.Id, vehicle.Id));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                async () => await duplicate.SaveChangesAsync(cancellationToken));

            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
            Assert.Equal("PK_user_vehicles", postgresException.ConstraintName);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserVehicles
                .Where(x => x.UserId == user.Id && x.VehicleId == vehicle.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Database_rejects_duplicate_provider_operation_ids()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationId = Guid.NewGuid();

        try
        {
            await using (var seed = fixture.CreateDbContext())
            {
                seed.ProviderOperations.Add(new ProviderOperation(
                    Guid.NewGuid(), operationId, null, null, ProviderOperationType.Start));
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var duplicate = fixture.CreateDbContext();
            duplicate.ProviderOperations.Add(new ProviderOperation(
                Guid.NewGuid(), operationId, null, null, ProviderOperationType.Start));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                async () => await duplicate.SaveChangesAsync(cancellationToken));

            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
            Assert.Equal("IX_provider_operations_OperationId", postgresException.ConstraintName);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.ProviderOperations
                .Where(x => x.OperationId == operationId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}