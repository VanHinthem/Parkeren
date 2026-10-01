using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parkeren.Application.Administration;
using Parkeren.Application.Visits;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Zones;
using Parkeren.Infrastructure;

namespace Parkeren.IntegrationTests.Database;

[Collection(PostgreSqlCollection.Name)]
public sealed class ParkingZoneAdministrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task New_default_zone_closes_previous_default_and_start_resolver_uses_new_zone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var reset = fixture.CreateDbContext())
            await reset.ParkingZones.ExecuteDeleteAsync(ct);

        var suffix = Guid.NewGuid().ToString("N");
        var admin = new User(Guid.NewGuid(), $"zone-admin-{suffix}", $"ZONE-ADMIN-{suffix}", "hash", UserRole.Admin);
        var visitor = new User(Guid.NewGuid(), $"zone-visitor-{suffix}", $"ZONE-VISITOR-{suffix}", "hash", UserRole.Visitor);
        var vehicle = new Vehicle(Guid.NewGuid(), $"ZN{suffix}"[..8], $"ZN{suffix}"[..8], null);
        var assignment = new UserVehicle(visitor.Id, vehicle.Id);
        var now = DateTimeOffset.UtcNow;
        var firstFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var secondFrom = now.AddDays(1);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Users.AddRange(admin, visitor);
            seed.Vehicles.Add(vehicle);
            seed.UserVehicles.Add(assignment);
            await seed.SaveChangesAsync(ct);
        }

        Guid? firstId = null;
        Guid? secondId = null;

        try
        {
            await using var provider = CreateServices().BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

            var first = await administration.CreateParkingZoneAsync(
                admin.Id,
                "Oss oud",
                "OSS_OLD",
                firstFrom,
                null,
                true,
                now,
                ct);
            Assert.Equal(AdminParkingZoneCreateOutcome.Created, first.Outcome);
            firstId = first.Zone!.Id;

            var second = await administration.CreateParkingZoneAsync(
                admin.Id,
                "Oss nieuw",
                "OSS_NEW",
                secondFrom,
                null,
                true,
                now,
                ct);
            Assert.Equal(AdminParkingZoneCreateOutcome.Created, second.Outcome);
            secondId = second.Zone!.Id;

            await using var verify = fixture.CreateDbContext();
            var persistedFirst = await verify.ParkingZones.SingleAsync(x => x.Id == firstId.Value, ct);
            var persistedSecond = await verify.ParkingZones.SingleAsync(x => x.Id == secondId.Value, ct);

            Assert.Equal(persistedSecond.ValidFrom, persistedFirst.ValidUntil);
            Assert.Null(persistedSecond.ValidUntil);

            var resolver = scope.ServiceProvider.GetRequiredService<IStartVisitRequestResolver>();
            var resolved = await resolver.ResolveAsync(
                visitor.Id,
                visitor.Id,
                vehicle.Id,
                secondFrom.AddMinutes(1),
                ct);

            Assert.NotNull(resolved);
            Assert.NotNull(resolved.ProviderContext);
            Assert.Equal(secondId, resolved.ProviderContext.ParkingZoneId);
            Assert.Equal("OSS_NEW", resolved.ProviderContext.Location);
            Assert.Equal(vehicle.LicensePlate, resolved.ProviderContext.LicensePlate);
        }
        finally
        {
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.UserVehicles
                .Where(x => x.UserId == visitor.Id && x.VehicleId == vehicle.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Users
                .Where(x => x.Id == admin.Id || x.Id == visitor.Id)
                .ExecuteDeleteAsync(ct);
            await cleanup.Vehicles.Where(x => x.Id == vehicle.Id).ExecuteDeleteAsync(ct);
            await cleanup.ParkingZones.ExecuteDeleteAsync(ct);
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
