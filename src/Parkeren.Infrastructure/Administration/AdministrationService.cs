using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Administration;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Administration;

internal sealed class AdministrationService(
    ParkerenDbContext dbContext,
    IPasswordHasher<User> passwordHasher) : IAdministrationService
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        return await dbContext.Users.AsNoTracking()
            .OrderBy(x => x.Username)
            .Select(x => new UserSummary(x.Id, x.Username, x.Role, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<CreateUserResult?> CreateUserAsync(
        Guid actorUserId, string username, string pin, UserRole role, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(username) || !IsValidPin(pin))
            return null;

        var trimmed = username.Trim();
        var normalized = NormalizeUsername(trimmed);
        if (await dbContext.Users.AnyAsync(x => x.NormalizedUsername == normalized, cancellationToken))
            return null;

        var user = new User(Guid.NewGuid(), trimmed, normalized, string.Empty, role);
        user.ChangePinHash(passwordHasher.HashPassword(user, pin));
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreateUserResult(user.Id, user.Username, user.Role, user.IsActive);
    }

    public async Task<bool> SetUserActiveAsync(Guid actorUserId, Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return false;

        if (isActive) user.Activate(); else user.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<VehicleSummary>> GetVehiclesAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        return await dbContext.Vehicles.AsNoTracking()
            .OrderBy(x => x.LicensePlate)
            .Select(x => new VehicleSummary(x.Id, x.LicensePlate, x.DisplayName, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<VehicleSummary?> CreateVehicleAsync(
        Guid actorUserId, string licensePlate, string? displayName, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(licensePlate))
            return null;

        var plate = licensePlate.Trim().ToUpperInvariant();
        var normalized = NormalizeLicensePlate(plate);
        if (normalized.Length is < 2 or > 16 ||
            await dbContext.Vehicles.AnyAsync(x => x.NormalizedLicensePlate == normalized, cancellationToken))
            return null;

        var vehicle = new Vehicle(Guid.NewGuid(), plate, normalized, string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim());
        dbContext.Vehicles.Add(vehicle);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new VehicleSummary(vehicle.Id, vehicle.LicensePlate, vehicle.DisplayName, vehicle.IsActive);
    }

    public async Task<bool> SetVehicleActiveAsync(Guid actorUserId, Guid vehicleId, bool isActive, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var vehicle = await dbContext.Vehicles.SingleOrDefaultAsync(x => x.Id == vehicleId, cancellationToken);
        if (vehicle is null)
            return false;

        if (isActive) vehicle.Activate(); else vehicle.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignVehicleAsync(Guid actorUserId, Guid userId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (!await dbContext.Users.AnyAsync(x => x.Id == userId, cancellationToken) ||
            !await dbContext.Vehicles.AnyAsync(x => x.Id == vehicleId, cancellationToken))
            return false;

        if (!await dbContext.UserVehicles.AnyAsync(x => x.UserId == userId && x.VehicleId == vehicleId, cancellationToken))
            dbContext.UserVehicles.Add(new UserVehicle(userId, vehicleId));

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnassignVehicleAsync(Guid actorUserId, Guid userId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var assignment = await dbContext.UserVehicles
            .SingleOrDefaultAsync(x => x.UserId == userId && x.VehicleId == vehicleId, cancellationToken);
        if (assignment is null)
            return false;

        dbContext.UserVehicles.Remove(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<VehicleSummary>?> GetAuthorizedVehiclesAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken))
            return null;

        return await dbContext.UserVehicles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Join(dbContext.Vehicles.Where(x => x.IsActive),
                assignment => assignment.VehicleId,
                vehicle => vehicle.Id,
                (_, vehicle) => new VehicleSummary(vehicle.Id, vehicle.LicensePlate, vehicle.DisplayName, vehicle.IsActive))
            .OrderBy(x => x.LicensePlate)
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureAdminAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(x => x.Id == actorUserId && x.IsActive && x.Role == UserRole.Admin, cancellationToken))
            throw new UnauthorizedAccessException("Active administrator required.");
    }

    private static bool IsValidPin(string pin) => pin.Length == 6 && pin.All(char.IsAsciiDigit);
    private static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
    private static string NormalizeLicensePlate(string plate) =>
        new(plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
