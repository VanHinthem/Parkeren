using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Administration;
using Parkeren.Application.Visits;
using Parkeren.Domain.Users;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Visits;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Administration;

internal sealed class AdministrationService(
    ParkerenDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IStartVisitOperationalContextResolver operationalContextResolver,
    IAdminAuditWriter auditWriter) : IAdministrationService
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        return await (
            from user in dbContext.Users.AsNoTracking()
            join policyOverride in dbContext.UserPolicyOverrides.AsNoTracking()
                on user.Id equals policyOverride.UserId into overrides
            from policyOverride in overrides.DefaultIfEmpty()
            orderby user.Username
            select new UserSummary(
                user.Id,
                user.Username,
                user.Role,
                user.IsActive,
                policyOverride == null ? null : policyOverride.MaxConcurrentVisits))
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);
        var user = new User(Guid.NewGuid(), trimmed, normalized, string.Empty, role);
        user.ChangePinHash(passwordHasher.HashPassword(user, pin));
        dbContext.Users.Add(user);
        if (role == UserRole.Admin)
        {
            var adminLimit = await dbContext.ParkingSystemSettings
                .Select(x => x.MaxConcurrentVisits).SingleAsync(cancellationToken);
            var adminPolicy = new UserPolicyOverride(user.Id);
            adminPolicy.SetMaxConcurrentVisits(adminLimit);
            dbContext.UserPolicyOverrides.Add(adminPolicy);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(
            actorUserId,
            "UserCreated",
            "User",
            user.Id.ToString(),
            new { user.Username, Role = user.Role.ToString(), user.IsActive },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateUserResult(user.Id, user.Username, user.Role, user.IsActive);
    }

    public async Task<bool> SetUserActiveAsync(Guid actorUserId, Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return false;

        if (!isActive && await dbContext.Visits.AnyAsync(
                x => x.UserId == userId &&
                     x.Status != VisitStatus.Completed &&
                     x.Status != VisitStatus.Cancelled,
                cancellationToken))
            throw new InvalidOperationException("Een gebruiker met een actieve Visit kan niet worden gedeactiveerd.");

        if (isActive) user.Activate(); else user.Deactivate();
        await auditWriter.WriteAsync(
            actorUserId,
            isActive ? "UserActivated" : "UserDeactivated",
            "User",
            user.Id.ToString(),
            new { user.Username },
            cancellationToken);
        return true;
    }

    public async Task<bool> SetUserMaxConcurrentVisitsAsync(
        Guid actorUserId, Guid userId, int? maxConcurrentVisits, CancellationToken cancellationToken)
    {
        var current = await dbContext.UserPolicyOverrides.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        var result = await SetUserPolicyAsync(
            actorUserId,
            userId,
            current?.MaxPaidParkingDurationMode ?? PolicyDurationOverrideMode.Inherit,
            ToMinutes(current?.MaxPaidParkingDuration),
            current?.MaxVisitElapsedDurationMode ?? PolicyDurationOverrideMode.Inherit,
            ToMinutes(current?.MaxVisitElapsedDuration),
            current?.AllowVisitExtension,
            current?.AllowOpenEndedVisits,
            maxConcurrentVisits,
            cancellationToken);

        return result.Outcome == AdminUserPolicyUpdateOutcome.Updated;
    }

    public async Task<int> GetGlobalMaxConcurrentVisitsAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        return await dbContext.ParkingSystemSettings.AsNoTracking()
            .Select(x => x.MaxConcurrentVisits).SingleAsync(cancellationToken);
    }

    public async Task<bool> SetGlobalMaxConcurrentVisitsAsync(
        Guid actorUserId, int maxConcurrentVisits, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (maxConcurrentVisits <= 0)
            return false;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);
        if (await dbContext.Visits.AnyAsync(
                x => x.Status != VisitStatus.Completed && x.Status != VisitStatus.Cancelled, cancellationToken))
            return false;

        var settings = await dbContext.ParkingSystemSettings.SingleAsync(cancellationToken);
        var previousMaxConcurrentVisits = settings.MaxConcurrentVisits;
        settings.SetMaxConcurrentVisits(maxConcurrentVisits);

        var defaults = await dbContext.DefaultParkingPolicies
            .OrderByDescending(x => x.UpdatedAt)
            .FirstAsync(cancellationToken);
        if (defaults.MaxConcurrentVisits > maxConcurrentVisits)
        {
            defaults.SetValues(
                defaults.MaxPaidParkingDuration,
                defaults.MaxVisitElapsedDuration,
                defaults.AllowVisitExtension,
                defaults.AllowOpenEndedVisits,
                maxConcurrentVisits);
        }

        var overrides = await dbContext.UserPolicyOverrides
            .Where(x => x.MaxConcurrentVisits > maxConcurrentVisits)
            .ToListAsync(cancellationToken);
        foreach (var policyOverride in overrides)
            policyOverride.SetMaxConcurrentVisits(maxConcurrentVisits);

        await auditWriter.WriteAsync(
            actorUserId,
            "GlobalParkingCapacityChanged",
            "ParkingSystemSettings",
            null,
            new
            {
                PreviousMaxConcurrentVisits = previousMaxConcurrentVisits,
                MaxConcurrentVisits = maxConcurrentVisits,
                DefaultPolicyAdjusted = defaults.MaxConcurrentVisits == maxConcurrentVisits &&
                    previousMaxConcurrentVisits != maxConcurrentVisits,
                AdjustedUserOverrideCount = overrides.Count
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private Task LockCapacitySettingsAsync(CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({0x5041524B})", cancellationToken);

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

        var normalized = Vehicle.NormalizeLicensePlate(licensePlate);
        if (normalized.Length is < 2 or > 16 ||
            await dbContext.Vehicles.AnyAsync(x => x.NormalizedLicensePlate == normalized, cancellationToken))
            return null;

        var vehicle = new Vehicle(Guid.NewGuid(), normalized, normalized, string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim());
        dbContext.Vehicles.Add(vehicle);
        await auditWriter.WriteAsync(
            actorUserId,
            "VehicleCreated",
            "Vehicle",
            vehicle.Id.ToString(),
            new { vehicle.LicensePlate, vehicle.DisplayName, vehicle.IsActive },
            cancellationToken);
        return new VehicleSummary(vehicle.Id, vehicle.LicensePlate, vehicle.DisplayName, vehicle.IsActive);
    }

    public async Task<bool> SetVehicleActiveAsync(Guid actorUserId, Guid vehicleId, bool isActive, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var vehicle = await dbContext.Vehicles.SingleOrDefaultAsync(x => x.Id == vehicleId, cancellationToken);
        if (vehicle is null)
            return false;

        if (!isActive && await dbContext.Visits.AnyAsync(
                x => x.VehicleId == vehicleId &&
                     x.Status != VisitStatus.Completed &&
                     x.Status != VisitStatus.Cancelled,
                cancellationToken))
            throw new InvalidOperationException("Een voertuig met een actieve Visit kan niet worden gedeactiveerd.");

        if (isActive) vehicle.Activate(); else vehicle.Deactivate();
        await auditWriter.WriteAsync(
            actorUserId,
            isActive ? "VehicleActivated" : "VehicleDeactivated",
            "Vehicle",
            vehicle.Id.ToString(),
            new { vehicle.LicensePlate },
            cancellationToken);
        return true;
    }

    public async Task<bool> AssignVehicleAsync(Guid actorUserId, Guid userId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        var vehicle = await dbContext.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == vehicleId, cancellationToken);
        if (user is null || vehicle is null)
            return false;

        if (await dbContext.UserVehicles.AnyAsync(
                x => x.UserId == userId && x.VehicleId == vehicleId,
                cancellationToken))
            return true;

        dbContext.UserVehicles.Add(new UserVehicle(userId, vehicleId));
        await auditWriter.WriteAsync(
            actorUserId,
            "VehicleAssigned",
            "UserVehicle",
            $"{userId}:{vehicleId}",
            new { UserId = user.Id, user.Username, VehicleId = vehicle.Id, vehicle.LicensePlate },
            cancellationToken);
        return true;
    }

    public async Task<bool> UnassignVehicleAsync(Guid actorUserId, Guid userId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        var assignment = await dbContext.UserVehicles
            .SingleOrDefaultAsync(x => x.UserId == userId && x.VehicleId == vehicleId, cancellationToken);
        if (assignment is null)
            return false;

        var user = await dbContext.Users.AsNoTracking()
            .SingleAsync(x => x.Id == userId, cancellationToken);
        var vehicle = await dbContext.Vehicles.AsNoTracking()
            .SingleAsync(x => x.Id == vehicleId, cancellationToken);

        dbContext.UserVehicles.Remove(assignment);
        await auditWriter.WriteAsync(
            actorUserId,
            "VehicleUnassigned",
            "UserVehicle",
            $"{userId}:{vehicleId}",
            new { UserId = user.Id, user.Username, VehicleId = vehicle.Id, vehicle.LicensePlate },
            cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<VehicleSummary>?> GetAuthorizedVehiclesAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(x => x.Id == userId && x.IsActive, cancellationToken))
            return null;

        var vehicleIds = await dbContext.UserVehicles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.VehicleId)
            .ToListAsync(cancellationToken);

        return await dbContext.Vehicles.AsNoTracking()
            .Where(x => x.IsActive && vehicleIds.Contains(x.Id))
            .OrderBy(x => x.LicensePlate)
            .Select(x => new VehicleSummary(x.Id, x.LicensePlate, x.DisplayName, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VehicleSummary>?> GetAssignedVehiclesAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (!await dbContext.Users.AnyAsync(x => x.Id == userId, cancellationToken))
            return null;

        var vehicleIds = await dbContext.UserVehicles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.VehicleId)
            .ToListAsync(cancellationToken);

        return await dbContext.Vehicles.AsNoTracking()
            .Where(x => vehicleIds.Contains(x.Id))
            .OrderBy(x => x.LicensePlate)
            .Select(x => new VehicleSummary(x.Id, x.LicensePlate, x.DisplayName, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminDashboardSummary> GetDashboardAsync(
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var active = await (
            from visit in dbContext.Visits.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on visit.UserId equals user.Id
            join vehicle in dbContext.Vehicles.AsNoTracking() on visit.VehicleId equals vehicle.Id
            where visit.Status == VisitStatus.Starting ||
                  visit.Status == VisitStatus.Active ||
                  visit.Status == VisitStatus.Stopping
            orderby visit.StartAt
            select new
            {
                Visit = visit,
                user.Username,
                vehicle.LicensePlate
            })
            .ToListAsync(cancellationToken);

        var total = await dbContext.ParkingSystemSettings.AsNoTracking()
            .Select(x => x.MaxConcurrentVisits)
            .SingleAsync(cancellationToken);

        if (active.Count == 0)
            return new AdminDashboardSummary(0, total, Array.Empty<AdminActiveVisitSummary>());

        var firstStart = active.Min(x => x.Visit.StartAt);
        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < now && (x.ValidUntil == null || x.ValidUntil > firstStart))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        var visits = active.Select(x =>
        {
            int? paidDurationMinutes = null;
            try
            {
                paidDurationMinutes = (int)Math.Floor(
                    ParkingRuleSetPaidTimeCalculator
                        .Calculate(x.Visit.StartAt, now, ruleSets)
                        .TotalMinutes);
            }
            catch (InvalidOperationException)
            {
                // Keep the operational dashboard available when historical rule coverage is incomplete.
            }

            return new AdminActiveVisitSummary(
                x.Visit.Id,
                x.Visit.UserId,
                x.Username,
                x.Visit.VehicleId,
                x.LicensePlate,
                x.Visit.StartAt,
                x.Visit.DesiredEndAt,
                x.Visit.Status,
                x.Visit.Health,
                paidDurationMinutes);
        }).ToArray();

        return new AdminDashboardSummary(visits.Length, total, visits);
    }

    public async Task<AdminParkingPolicySummary?> GetUserParkingPolicyAsync(
        Guid actorUserId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var isActiveVisitor = await dbContext.Users.AsNoTracking()
            .AnyAsync(x => x.Id == userId && x.IsActive && x.Role == UserRole.Visitor, cancellationToken);
        if (!isActiveVisitor)
            return null;

        var context = await operationalContextResolver.ResolveAsync(
            userId,
            now,
            desiredEndAt: null,
            cancellationToken);
        if (context is null)
            return null;

        return new AdminParkingPolicySummary(
            context.Policy.MaxPaidParkingDuration is null
                ? null
                : (int)context.Policy.MaxPaidParkingDuration.Value.TotalMinutes,
            context.Policy.MaxVisitElapsedDuration is null
                ? null
                : (int)context.Policy.MaxVisitElapsedDuration.Value.TotalMinutes,
            context.Policy.AllowVisitExtension,
            context.Policy.AllowOpenEndedVisits,
            context.Policy.MaxConcurrentVisits);
    }

    public async Task<AdminUserDetail?> GetUserDetailAsync(
        Guid actorUserId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return null;

        var policyOverride = await dbContext.UserPolicyOverrides.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var defaults = await dbContext.DefaultParkingPolicies.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .FirstAsync(cancellationToken);
        var globalLimit = await dbContext.ParkingSystemSettings.AsNoTracking()
            .Select(x => x.MaxConcurrentVisits)
            .SingleAsync(cancellationToken);
        var assignedVehicles = await GetAssignedVehiclesAsync(actorUserId, userId, cancellationToken)
            ?? Array.Empty<VehicleSummary>();
        var activeVisitCount = await dbContext.Visits.AsNoTracking()
            .CountAsync(
                x => x.UserId == userId &&
                     x.Status != VisitStatus.Completed &&
                     x.Status != VisitStatus.Cancelled,
                cancellationToken);

        return new AdminUserDetail(
            new UserSummary(user.Id, user.Username, user.Role, user.IsActive, policyOverride?.MaxConcurrentVisits),
            assignedVehicles,
            CreatePolicyDetail(defaults, policyOverride, globalLimit),
            activeVisitCount);
    }

    public async Task<AdminUserPolicyUpdateResult> SetUserPolicyAsync(
        Guid actorUserId,
        Guid userId,
        PolicyDurationOverrideMode maxPaidParkingDurationMode,
        int? maxPaidParkingDurationMinutes,
        PolicyDurationOverrideMode maxVisitElapsedDurationMode,
        int? maxVisitElapsedDurationMinutes,
        bool? allowVisitExtension,
        bool? allowOpenEndedVisits,
        int? maxConcurrentVisits,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        if (!IsValidDurationOverride(maxPaidParkingDurationMode, maxPaidParkingDurationMinutes) ||
            !IsValidDurationOverride(maxVisitElapsedDurationMode, maxVisitElapsedDurationMinutes) ||
            maxConcurrentVisits <= 0)
            return new AdminUserPolicyUpdateResult(AdminUserPolicyUpdateOutcome.Invalid, 0, null);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return new AdminUserPolicyUpdateResult(AdminUserPolicyUpdateOutcome.NotFound, 0, null);

        var defaults = await dbContext.DefaultParkingPolicies
            .OrderByDescending(x => x.UpdatedAt)
            .FirstAsync(cancellationToken);
        var globalLimit = await dbContext.ParkingSystemSettings
            .Select(x => x.MaxConcurrentVisits)
            .SingleAsync(cancellationToken);
        if (maxConcurrentVisits > globalLimit)
            return new AdminUserPolicyUpdateResult(AdminUserPolicyUpdateOutcome.Invalid, 0, null);

        var policyOverride = await dbContext.UserPolicyOverrides
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        var currentEffective = ParkingPolicyResolver.Resolve(defaults, policyOverride);

        var proposedOverride = new UserPolicyOverride(userId);
        proposedOverride.SetOverrides(
            maxPaidParkingDurationMode,
            ToDuration(maxPaidParkingDurationMinutes),
            maxVisitElapsedDurationMode,
            ToDuration(maxVisitElapsedDurationMinutes),
            allowVisitExtension,
            allowOpenEndedVisits,
            maxConcurrentVisits);
        var proposedEffective = ParkingPolicyResolver.Resolve(defaults, proposedOverride);

        var activeVisitCount = await dbContext.Visits
            .CountAsync(
                x => x.UserId == userId &&
                     x.Status != VisitStatus.Completed &&
                     x.Status != VisitStatus.Cancelled,
                cancellationToken);
        if (activeVisitCount > 0 && proposedEffective != currentEffective)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AdminUserPolicyUpdateResult(
                AdminUserPolicyUpdateOutcome.ActiveVisitConflict,
                activeVisitCount,
                CreatePolicyDetail(defaults, policyOverride, globalLimit));
        }

        if (!proposedOverride.HasAnyOverride)
        {
            if (policyOverride is not null)
                dbContext.UserPolicyOverrides.Remove(policyOverride);
            policyOverride = null;
        }
        else
        {
            policyOverride ??= new UserPolicyOverride(userId);
            if (dbContext.Entry(policyOverride).State == EntityState.Detached)
                dbContext.UserPolicyOverrides.Add(policyOverride);

            policyOverride.SetOverrides(
                maxPaidParkingDurationMode,
                ToDuration(maxPaidParkingDurationMinutes),
                maxVisitElapsedDurationMode,
                ToDuration(maxVisitElapsedDurationMinutes),
                allowVisitExtension,
                allowOpenEndedVisits,
                maxConcurrentVisits);
        }

        await auditWriter.WriteAsync(
            actorUserId,
            "UserPolicyChanged",
            "User",
            user.Id.ToString(),
            new
            {
                user.Username,
                MaxPaidParkingDurationMode = maxPaidParkingDurationMode.ToString(),
                MaxPaidParkingDurationMinutes = maxPaidParkingDurationMinutes,
                MaxVisitElapsedDurationMode = maxVisitElapsedDurationMode.ToString(),
                MaxVisitElapsedDurationMinutes = maxVisitElapsedDurationMinutes,
                AllowVisitExtension = allowVisitExtension,
                AllowOpenEndedVisits = allowOpenEndedVisits,
                MaxConcurrentVisits = maxConcurrentVisits
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminUserPolicyUpdateResult(
            AdminUserPolicyUpdateOutcome.Updated,
            activeVisitCount,
            CreatePolicyDetail(defaults, policyOverride, globalLimit));
    }

    private static AdminUserPolicyDetail CreatePolicyDetail(
        DefaultParkingPolicy defaults,
        UserPolicyOverride? policyOverride,
        int globalLimit)
    {
        var effective = ParkingPolicyResolver.Resolve(defaults, policyOverride);
        return new AdminUserPolicyDetail(
            new AdminParkingPolicyValues(
                ToMinutes(defaults.MaxPaidParkingDuration),
                ToMinutes(defaults.MaxVisitElapsedDuration),
                defaults.AllowVisitExtension,
                defaults.AllowOpenEndedVisits,
                defaults.MaxConcurrentVisits),
            new AdminParkingPolicyOverrideValues(
                policyOverride?.MaxPaidParkingDurationMode ?? PolicyDurationOverrideMode.Inherit,
                ToMinutes(policyOverride?.MaxPaidParkingDuration),
                policyOverride?.MaxVisitElapsedDurationMode ?? PolicyDurationOverrideMode.Inherit,
                ToMinutes(policyOverride?.MaxVisitElapsedDuration),
                policyOverride?.AllowVisitExtension,
                policyOverride?.AllowOpenEndedVisits,
                policyOverride?.MaxConcurrentVisits),
            new AdminParkingPolicyValues(
                ToMinutes(effective.MaxPaidParkingDuration),
                ToMinutes(effective.MaxVisitElapsedDuration),
                effective.AllowVisitExtension,
                effective.AllowOpenEndedVisits,
                effective.MaxConcurrentVisits),
            globalLimit);
    }

    private static int? ToMinutes(TimeSpan? value) =>
        value is null ? null : (int)value.Value.TotalMinutes;

    private static TimeSpan? ToDuration(int? minutes) =>
        minutes is null ? null : TimeSpan.FromMinutes(minutes.Value);

    private static bool IsValidDurationOverride(PolicyDurationOverrideMode mode, int? minutes) =>
        mode switch
        {
            PolicyDurationOverrideMode.Inherit => minutes is null,
            PolicyDurationOverrideMode.Unlimited => minutes is null,
            PolicyDurationOverrideMode.Value => minutes is > 0,
            _ => false
        };

    public async Task<AdminSystemSettingsSummary> GetSystemSettingsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var defaults = await dbContext.DefaultParkingPolicies.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .FirstAsync(cancellationToken);
        var settings = await dbContext.ParkingSystemSettings.AsNoTracking()
            .SingleAsync(cancellationToken);

        return CreateSystemSettingsSummary(defaults, settings);
    }

    public async Task<AdminDefaultPolicyUpdateResult> SetDefaultParkingPolicyAsync(
        Guid actorUserId,
        int? maxPaidParkingDurationMinutes,
        int? maxVisitElapsedDurationMinutes,
        bool allowVisitExtension,
        bool allowOpenEndedVisits,
        int maxConcurrentVisits,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        if (maxPaidParkingDurationMinutes <= 0 ||
            maxVisitElapsedDurationMinutes <= 0 ||
            maxConcurrentVisits <= 0)
        {
            var current = await dbContext.DefaultParkingPolicies.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .FirstAsync(cancellationToken);
            return new AdminDefaultPolicyUpdateResult(
                AdminDefaultPolicyUpdateOutcome.Invalid,
                0,
                Array.Empty<AdminDefaultPolicyField>(),
                ToAdminParkingPolicyValues(current));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);

        var defaults = await dbContext.DefaultParkingPolicies
            .OrderByDescending(x => x.UpdatedAt)
            .FirstAsync(cancellationToken);
        var globalLimit = await dbContext.ParkingSystemSettings
            .Select(x => x.MaxConcurrentVisits)
            .SingleAsync(cancellationToken);

        if (maxConcurrentVisits > globalLimit)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AdminDefaultPolicyUpdateResult(
                AdminDefaultPolicyUpdateOutcome.Invalid,
                0,
                Array.Empty<AdminDefaultPolicyField>(),
                ToAdminParkingPolicyValues(defaults));
        }

        var proposedPaid = ToDuration(maxPaidParkingDurationMinutes);
        var proposedElapsed = ToDuration(maxVisitElapsedDurationMinutes);

        var changedFields = new HashSet<AdminDefaultPolicyField>();
        if (defaults.MaxPaidParkingDuration != proposedPaid)
            changedFields.Add(AdminDefaultPolicyField.MaxPaidParkingDuration);
        if (defaults.MaxVisitElapsedDuration != proposedElapsed)
            changedFields.Add(AdminDefaultPolicyField.MaxVisitElapsedDuration);
        if (defaults.AllowVisitExtension != allowVisitExtension)
            changedFields.Add(AdminDefaultPolicyField.AllowVisitExtension);
        if (defaults.AllowOpenEndedVisits != allowOpenEndedVisits)
            changedFields.Add(AdminDefaultPolicyField.AllowOpenEndedVisits);
        if (defaults.MaxConcurrentVisits != maxConcurrentVisits)
            changedFields.Add(AdminDefaultPolicyField.MaxConcurrentVisits);

        if (changedFields.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AdminDefaultPolicyUpdateResult(
                AdminDefaultPolicyUpdateOutcome.Updated,
                0,
                Array.Empty<AdminDefaultPolicyField>(),
                ToAdminParkingPolicyValues(defaults));
        }

        var activeVisits = await dbContext.Visits.AsNoTracking()
            .Where(x => x.Status != VisitStatus.Completed && x.Status != VisitStatus.Cancelled)
            .GroupBy(x => x.UserId)
            .Select(x => new { UserId = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        if (activeVisits.Count > 0)
        {
            var activeUserIds = activeVisits.Select(x => x.UserId).ToArray();
            var overrides = await dbContext.UserPolicyOverrides.AsNoTracking()
                .Where(x => activeUserIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, cancellationToken);

            var blockedFields = new HashSet<AdminDefaultPolicyField>();
            var affectedVisitCount = 0;

            foreach (var activeUser in activeVisits)
            {
                overrides.TryGetValue(activeUser.UserId, out var policyOverride);
                var userAffected = false;

                foreach (var field in changedFields)
                {
                    if (!InheritsDefault(policyOverride, field))
                        continue;

                    blockedFields.Add(field);
                    userAffected = true;
                }

                if (userAffected)
                    affectedVisitCount += activeUser.Count;
            }

            if (blockedFields.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminDefaultPolicyUpdateResult(
                    AdminDefaultPolicyUpdateOutcome.ActiveVisitConflict,
                    affectedVisitCount,
                    blockedFields.OrderBy(x => x).ToArray(),
                    ToAdminParkingPolicyValues(defaults));
            }
        }

        defaults.SetValues(
            proposedPaid,
            proposedElapsed,
            allowVisitExtension,
            allowOpenEndedVisits,
            maxConcurrentVisits);

        await auditWriter.WriteAsync(
            actorUserId,
            "DefaultParkingPolicyChanged",
            "DefaultParkingPolicy",
            defaults.Id.ToString(),
            new
            {
                ChangedFields = changedFields.OrderBy(x => x).Select(x => x.ToString()).ToArray(),
                MaxPaidParkingDurationMinutes = maxPaidParkingDurationMinutes,
                MaxVisitElapsedDurationMinutes = maxVisitElapsedDurationMinutes,
                AllowVisitExtension = allowVisitExtension,
                AllowOpenEndedVisits = allowOpenEndedVisits,
                MaxConcurrentVisits = maxConcurrentVisits
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminDefaultPolicyUpdateResult(
            AdminDefaultPolicyUpdateOutcome.Updated,
            0,
            Array.Empty<AdminDefaultPolicyField>(),
            ToAdminParkingPolicyValues(defaults));
    }

    public async Task<AdminWarningSettingsUpdateResult> SetWarningSettingsAsync(
        Guid actorUserId,
        int? longVisitWarningAfterMinutes,
        bool notifyAdminOnLongVisit,
        int? longVisitReminderIntervalMinutes,
        IReadOnlyList<int> budgetWarningThresholdPercentages,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var settings = await dbContext.ParkingSystemSettings.SingleAsync(cancellationToken);
        if (longVisitWarningAfterMinutes <= 0 ||
            longVisitReminderIntervalMinutes <= 0 ||
            budgetWarningThresholdPercentages is null ||
            budgetWarningThresholdPercentages.Count == 0 ||
            budgetWarningThresholdPercentages.Any(x => x <= 0 || x > 100) ||
            budgetWarningThresholdPercentages.Distinct().Count() != budgetWarningThresholdPercentages.Count)
        {
            var defaults = await dbContext.DefaultParkingPolicies.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .FirstAsync(cancellationToken);
            return new AdminWarningSettingsUpdateResult(
                AdminWarningSettingsUpdateOutcome.Invalid,
                CreateSystemSettingsSummary(defaults, settings));
        }

        settings.SetLongVisitNotifications(
            ToDuration(longVisitWarningAfterMinutes),
            notifyAdminOnLongVisit,
            ToDuration(longVisitReminderIntervalMinutes));
        settings.SetBudgetWarningThresholdPercentages(budgetWarningThresholdPercentages);

        await auditWriter.WriteAsync(
            actorUserId,
            "WarningSettingsChanged",
            "ParkingSystemSettings",
            null,
            new
            {
                LongVisitWarningAfterMinutes = longVisitWarningAfterMinutes,
                NotifyAdminOnLongVisit = notifyAdminOnLongVisit,
                LongVisitReminderIntervalMinutes = longVisitReminderIntervalMinutes,
                BudgetWarningThresholdPercentages = budgetWarningThresholdPercentages
            },
            cancellationToken);

        var currentDefaults = await dbContext.DefaultParkingPolicies.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .FirstAsync(cancellationToken);
        return new AdminWarningSettingsUpdateResult(
            AdminWarningSettingsUpdateOutcome.Updated,
            CreateSystemSettingsSummary(currentDefaults, settings));
    }

    private static bool InheritsDefault(
        UserPolicyOverride? policyOverride,
        AdminDefaultPolicyField field) =>
        field switch
        {
            AdminDefaultPolicyField.MaxPaidParkingDuration =>
                policyOverride is null ||
                policyOverride.MaxPaidParkingDurationMode == PolicyDurationOverrideMode.Inherit,
            AdminDefaultPolicyField.MaxVisitElapsedDuration =>
                policyOverride is null ||
                policyOverride.MaxVisitElapsedDurationMode == PolicyDurationOverrideMode.Inherit,
            AdminDefaultPolicyField.AllowVisitExtension =>
                policyOverride is null || policyOverride.AllowVisitExtension is null,
            AdminDefaultPolicyField.AllowOpenEndedVisits =>
                policyOverride is null || policyOverride.AllowOpenEndedVisits is null,
            AdminDefaultPolicyField.MaxConcurrentVisits =>
                policyOverride is null || policyOverride.MaxConcurrentVisits is null,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

    private static AdminParkingPolicyValues ToAdminParkingPolicyValues(DefaultParkingPolicy defaults) =>
        new(
            ToMinutes(defaults.MaxPaidParkingDuration),
            ToMinutes(defaults.MaxVisitElapsedDuration),
            defaults.AllowVisitExtension,
            defaults.AllowOpenEndedVisits,
            defaults.MaxConcurrentVisits);

    private static AdminSystemSettingsSummary CreateSystemSettingsSummary(
        DefaultParkingPolicy defaults,
        ParkingSystemSettings settings) =>
        new(
            ToAdminParkingPolicyValues(defaults),
            defaults.UpdatedAt,
            settings.MaxConcurrentVisits,
            ToMinutes(settings.LongVisitWarningAfter),
            settings.NotifyAdminOnLongVisit,
            ToMinutes(settings.LongVisitReminderInterval),
            settings.BudgetWarningThresholdPercentages,
            settings.UpdatedAt);

    public async Task<IReadOnlyList<AdminParkingRuleSetVersion>> GetParkingRuleSetsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var ruleSets = await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .OrderByDescending(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        return ruleSets.Select(ToAdminParkingRuleSetVersion).ToArray();
    }

    public async Task<IReadOnlyList<AdminParkingRuleSetVersion>> GetParkingRuleSetsForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        return (await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ProviderProductId == providerProductId)
            .OrderByDescending(x => x.ValidFrom)
            .ToListAsync(cancellationToken))
            .Select(ToAdminParkingRuleSetVersion)
            .ToArray();
    }

    public async Task<AdminParkingRuleSetCreateResult> CreateParkingRuleSetVersionAsync(
        Guid actorUserId,
        DateTimeOffset validFrom,
        int maxProviderActionDurationMinutes,
        ProviderCoverageContinuation continuation,
        bool publicHolidaysAreFree,
        IReadOnlyList<AdminPaidWindowInput> paidWindows,
        IReadOnlyList<AdminCalendarExceptionInput> calendarExceptions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        if (validFrom <= now)
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.MustBeFuture, null);
        if (maxProviderActionDurationMinutes <= 0 ||
            paidWindows is null ||
            calendarExceptions is null ||
            !Enum.IsDefined(continuation))
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.Invalid, null);

        ParkingRuleSet proposed;
        try
        {
            proposed = new ParkingRuleSet(
                Guid.NewGuid(),
                validFrom,
                validUntil: null,
                TimeSpan.FromMinutes(maxProviderActionDurationMinutes),
                paidWindows.Select(x => new PaidWindow(x.Day, x.Start, x.End)).ToArray(),
                calendarExceptions.Select(x => new ParkingCalendarException(x.Date, x.IsPaid)).ToArray(),
                publicHolidaysAreFree,
                continuation);
        }
        catch (ArgumentException)
        {
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.Invalid, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);

        var latest = await dbContext.ParkingRuleSets
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null)
        {
            if (latest.ValidUntil.HasValue || validFrom <= latest.ValidFrom)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.SequenceConflict, null);
            }

            latest.CloseAt(validFrom);
        }

        dbContext.ParkingRuleSets.Add(proposed);
        await auditWriter.WriteAsync(
            actorUserId,
            "ParkingRuleSetCreated",
            "ParkingRuleSet",
            proposed.Id.ToString(),
            new
            {
                ProviderProductId = proposed.ProviderProductId,
                proposed.ValidFrom,
                MaxProviderActionDurationMinutes = maxProviderActionDurationMinutes,
                Continuation = continuation.ToString(),
                PublicHolidaysAreFree = publicHolidaysAreFree,
                PaidWindowCount = paidWindows.Count,
                CalendarExceptionCount = calendarExceptions.Count
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminParkingRuleSetCreateResult(
            AdminParkingRuleSetCreateOutcome.Created,
            ToAdminParkingRuleSetVersion(proposed));
    }

    public async Task<AdminParkingRuleSetCreateResult> CreateParkingRuleSetVersionForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        DateTimeOffset validFrom,
        int maxProviderActionDurationMinutes,
        ProviderCoverageContinuation continuation,
        bool publicHolidaysAreFree,
        IReadOnlyList<AdminPaidWindowInput> paidWindows,
        IReadOnlyList<AdminCalendarExceptionInput> calendarExceptions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var productExists = await dbContext.ParkingProviderProducts.AsNoTracking()
            .AnyAsync(x => x.Id == providerProductId && x.IsAvailable, cancellationToken);
        if (!productExists)
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.Invalid, null);

        if (validFrom <= now)
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.MustBeFuture, null);
        if (maxProviderActionDurationMinutes <= 0 ||
            paidWindows is null ||
            calendarExceptions is null ||
            !Enum.IsDefined(continuation))
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.Invalid, null);

        ParkingRuleSet proposed;
        try
        {
            proposed = new ParkingRuleSet(
                Guid.NewGuid(),
                validFrom,
                validUntil: null,
                TimeSpan.FromMinutes(maxProviderActionDurationMinutes),
                paidWindows.Select(x => new PaidWindow(x.Day, x.Start, x.End)).ToArray(),
                calendarExceptions.Select(x => new ParkingCalendarException(x.Date, x.IsPaid)).ToArray(),
                publicHolidaysAreFree,
                continuation);
            proposed.AssignProviderProduct(providerProductId);
        }
        catch (ArgumentException)
        {
            return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.Invalid, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);

        var latest = await dbContext.ParkingRuleSets
            .Where(x => x.ProviderProductId == providerProductId)
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null)
        {
            if (latest.ValidUntil.HasValue || validFrom <= latest.ValidFrom)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminParkingRuleSetCreateResult(AdminParkingRuleSetCreateOutcome.SequenceConflict, null);
            }

            latest.CloseAt(validFrom);
        }

        dbContext.ParkingRuleSets.Add(proposed);
        await auditWriter.WriteAsync(
            actorUserId,
            "ParkingRuleSetCreated",
            "ParkingRuleSet",
            proposed.Id.ToString(),
            new
            {
                ProviderProductId = proposed.ProviderProductId,
                proposed.ValidFrom,
                MaxProviderActionDurationMinutes = maxProviderActionDurationMinutes,
                Continuation = continuation.ToString(),
                PublicHolidaysAreFree = publicHolidaysAreFree,
                PaidWindowCount = paidWindows.Count,
                CalendarExceptionCount = calendarExceptions.Count
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminParkingRuleSetCreateResult(
            AdminParkingRuleSetCreateOutcome.Created,
            ToAdminParkingRuleSetVersion(proposed));
    }

    private static AdminParkingRuleSetVersion ToAdminParkingRuleSetVersion(ParkingRuleSet ruleSet) =>
        new(
            ruleSet.Id,
            ruleSet.ProviderProductId,
            ruleSet.ValidFrom,
            ruleSet.ValidUntil,
            (int)ruleSet.MaxProviderActionDuration.TotalMinutes,
            ruleSet.Continuation,
            ruleSet.PublicHolidaysAreFree,
            ruleSet.PaidWindows
                .OrderBy(x => x.Day)
                .ThenBy(x => x.Start)
                .Select(x => new AdminPaidWindowSummary(x.Id, x.Day, x.Start, x.End))
                .ToArray(),
            ruleSet.CalendarExceptions
                .OrderBy(x => x.Date)
                .Select(x => new AdminCalendarExceptionSummary(x.Id, x.Date, x.IsPaid))
                .ToArray());

    public async Task<IReadOnlyList<AdminBudgetPeriodSummary>> GetBudgetPeriodsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        return await dbContext.ParkingBudgetPeriods.AsNoTracking()
            .OrderByDescending(x => x.ValidFrom)
            .Select(x => new AdminBudgetPeriodSummary(
                x.Id,
                x.ProviderProductId,
                x.ValidFrom,
                x.ValidUntil,
                (int)x.MaximumPaidDuration.TotalMinutes))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminBudgetPeriodSummary>> GetBudgetPeriodsForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        return await dbContext.ParkingBudgetPeriods.AsNoTracking()
            .Where(x => x.ProviderProductId == providerProductId)
            .OrderByDescending(x => x.ValidFrom)
            .Select(x => new AdminBudgetPeriodSummary(
                x.Id,
                x.ProviderProductId,
                x.ValidFrom,
                x.ValidUntil,
                (int)x.MaximumPaidDuration.TotalMinutes))
            .ToListAsync(cancellationToken);
    }

    public Task<AdminBudgetPeriodCreateResult> CreateBudgetPeriodAsync(
        Guid actorUserId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        int maximumPaidDurationMinutes,
        CancellationToken cancellationToken) =>
        CreateBudgetPeriodInternalAsync(
            actorUserId,
            null,
            validFrom,
            validUntil,
            maximumPaidDurationMinutes,
            cancellationToken);

    public async Task<AdminBudgetPeriodCreateResult> CreateBudgetPeriodForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        int maximumPaidDurationMinutes,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (!await dbContext.ParkingProviderProducts.AsNoTracking()
                .AnyAsync(x => x.Id == providerProductId && x.IsAvailable, cancellationToken))
            return new AdminBudgetPeriodCreateResult(AdminBudgetPeriodCreateOutcome.Invalid, null);

        return await CreateBudgetPeriodInternalAsync(
            actorUserId,
            providerProductId,
            validFrom,
            validUntil,
            maximumPaidDurationMinutes,
            cancellationToken);
    }

    private async Task<AdminBudgetPeriodCreateResult> CreateBudgetPeriodInternalAsync(
        Guid actorUserId,
        Guid? providerProductId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        int maximumPaidDurationMinutes,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        if (validUntil <= validFrom || maximumPaidDurationMinutes <= 0)
            return new AdminBudgetPeriodCreateResult(AdminBudgetPeriodCreateOutcome.Invalid, null);

        var proposed = new ParkingBudgetPeriod(
            Guid.NewGuid(),
            validFrom,
            validUntil,
            TimeSpan.FromMinutes(maximumPaidDurationMinutes));
        if (providerProductId.HasValue)
            proposed.AssignProviderProduct(providerProductId.Value);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);

        var existing = await dbContext.ParkingBudgetPeriods.AsNoTracking()
            .Where(x => x.ProviderProductId == providerProductId)
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        try
        {
            ParkingBudgetPeriodResolver.ValidateNoOverlap(existing.Append(proposed));
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AdminBudgetPeriodCreateResult(AdminBudgetPeriodCreateOutcome.Overlap, null);
        }

        dbContext.ParkingBudgetPeriods.Add(proposed);
        await auditWriter.WriteAsync(
            actorUserId,
            "ParkingBudgetCreated",
            "ParkingBudgetPeriod",
            proposed.Id.ToString(),
            new
            {
                ProviderProductId = proposed.ProviderProductId,
                proposed.ValidFrom,
                proposed.ValidUntil,
                MaximumPaidDurationMinutes = maximumPaidDurationMinutes
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminBudgetPeriodCreateResult(
            AdminBudgetPeriodCreateOutcome.Created,
            ToAdminBudgetPeriodSummary(proposed));
    }

    public async Task<AdminBudgetUsageSummary?> GetBudgetUsageAsync(
        Guid actorUserId,
        Guid? budgetPeriodId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        ParkingBudgetPeriod? period;
        if (budgetPeriodId.HasValue)
        {
            period = await dbContext.ParkingBudgetPeriods.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == budgetPeriodId.Value, cancellationToken);
        }
        else
        {
            var defaultProductId = await dbContext.ParkingProviderProducts.AsNoTracking()
                .Where(x => x.IsDefault)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            var hasProviderProducts = await dbContext.ParkingProviderProducts.AsNoTracking()
                .AnyAsync(cancellationToken);

            if (hasProviderProducts && !defaultProductId.HasValue)
                return null;

            period = await dbContext.ParkingBudgetPeriods.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ProviderProductId == defaultProductId &&
                         x.ValidFrom <= now &&
                         now < x.ValidUntil,
                    cancellationToken);
        }

        if (period is null)
            return null;

        var visits = await dbContext.Visits.AsNoTracking()
            .Where(x => x.ProviderProductId == period.ProviderProductId &&
                        x.Status == VisitStatus.Completed &&
                        x.ActualEndAt.HasValue &&
                        ((x.StartAt < period.ValidUntil && x.ActualEndAt.Value > period.ValidFrom) ||
                         dbContext.ProviderParkingActions.Any(action =>
                             action.VisitId == x.Id &&
                             action.ActualStartAt.HasValue &&
                             action.ActualEndAt.HasValue &&
                             action.ActualStartAt.Value < period.ValidUntil &&
                             action.ActualEndAt.Value > period.ValidFrom)))
            .ToListAsync(cancellationToken);

        var ruleSets = await LoadRuleSetsForProductAsync(
            period.ValidFrom,
            period.ValidUntil,
            period.ProviderProductId,
            cancellationToken);
        var visitIds = visits.Select(x => x.Id).ToArray();
        var providerActions = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
            .ToListAsync(cancellationToken);

        try
        {
            var usage = RealizedParkingBudgetUsageCalculator.Calculate(period, visits, providerActions, ruleSets);
            return new AdminBudgetUsageSummary(
                ToAdminBudgetPeriodSummary(period),
                (int)Math.Floor(usage.UsedPaidDuration.TotalMinutes),
                (int)Math.Floor(usage.RemainingPaidDuration.TotalMinutes),
                true,
                null);
        }
        catch (InvalidOperationException exception)
        {
            return new AdminBudgetUsageSummary(
                ToAdminBudgetPeriodSummary(period),
                null,
                null,
                false,
                exception.Message);
        }
    }

    public async Task<IReadOnlyList<AdminParkingTariffSummary>> GetParkingTariffsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        return await dbContext.ParkingTariffs.AsNoTracking()
            .OrderByDescending(x => x.ValidFrom)
            .Select(x => new AdminParkingTariffSummary(
                x.Id,
                x.ProviderProductId,
                x.ValidFrom,
                x.ValidUntil,
                x.Rate,
                x.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminParkingTariffSummary>> GetParkingTariffsForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        return await dbContext.ParkingTariffs.AsNoTracking()
            .Where(x => x.ProviderProductId == providerProductId)
            .OrderByDescending(x => x.ValidFrom)
            .Select(x => new AdminParkingTariffSummary(
                x.Id,
                x.ProviderProductId,
                x.ValidFrom,
                x.ValidUntil,
                x.Rate,
                x.Unit))
            .ToListAsync(cancellationToken);
    }

    public Task<AdminParkingTariffCreateResult> CreateParkingTariffAsync(
        Guid actorUserId,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        decimal rate,
        ParkingTariffUnit unit,
        CancellationToken cancellationToken) =>
        CreateParkingTariffInternalAsync(
            actorUserId,
            null,
            validFrom,
            validUntil,
            rate,
            unit,
            cancellationToken);

    public async Task<AdminParkingTariffCreateResult> CreateParkingTariffForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        decimal rate,
        ParkingTariffUnit unit,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (!await dbContext.ParkingProviderProducts.AsNoTracking()
                .AnyAsync(x => x.Id == providerProductId && x.IsAvailable, cancellationToken))
            return new AdminParkingTariffCreateResult(AdminParkingTariffCreateOutcome.Invalid, null);

        return await CreateParkingTariffInternalAsync(
            actorUserId,
            providerProductId,
            validFrom,
            validUntil,
            rate,
            unit,
            cancellationToken);
    }

    private async Task<AdminParkingTariffCreateResult> CreateParkingTariffInternalAsync(
        Guid actorUserId,
        Guid? providerProductId,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        decimal rate,
        ParkingTariffUnit unit,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        if ((validUntil.HasValue && validUntil.Value <= validFrom) ||
            rate < 0m ||
            !Enum.IsDefined(unit))
            return new AdminParkingTariffCreateResult(AdminParkingTariffCreateOutcome.Invalid, null);

        ParkingTariff proposed;
        try
        {
            proposed = new ParkingTariff(Guid.NewGuid(), validFrom, validUntil, rate, unit);
            if (providerProductId.HasValue)
                proposed.AssignProviderProduct(providerProductId.Value);
        }
        catch (ArgumentException)
        {
            return new AdminParkingTariffCreateResult(AdminParkingTariffCreateOutcome.Invalid, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockCapacitySettingsAsync(cancellationToken);

        var existing = await dbContext.ParkingTariffs
            .Where(x => x.ProviderProductId == providerProductId)
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        ParkingTariff? latestOpenToClose = null;
        var validationTariffs = existing.Cast<ParkingTariff>().ToList();
        if (!validUntil.HasValue)
        {
            latestOpenToClose = existing
                .Where(x => !x.ValidUntil.HasValue)
                .OrderByDescending(x => x.ValidFrom)
                .FirstOrDefault();
            if (latestOpenToClose is not null && validFrom > latestOpenToClose.ValidFrom)
            {
                validationTariffs.Remove(latestOpenToClose);
                var closed = new ParkingTariff(
                    latestOpenToClose.Id,
                    latestOpenToClose.ValidFrom,
                    validFrom,
                    latestOpenToClose.Rate,
                    latestOpenToClose.Unit);
                if (providerProductId.HasValue)
                    closed.AssignProviderProduct(providerProductId.Value);
                validationTariffs.Add(closed);
            }
        }

        try
        {
            ParkingTariffResolver.ValidateNoOverlap(validationTariffs.Append(proposed));
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AdminParkingTariffCreateResult(AdminParkingTariffCreateOutcome.Overlap, null);
        }

        if (latestOpenToClose is not null && validFrom > latestOpenToClose.ValidFrom)
            latestOpenToClose.CloseAt(validFrom);

        dbContext.ParkingTariffs.Add(proposed);
        await auditWriter.WriteAsync(
            actorUserId,
            "ParkingTariffCreated",
            "ParkingTariff",
            proposed.Id.ToString(),
            new
            {
                ProviderProductId = proposed.ProviderProductId,
                proposed.ValidFrom,
                proposed.ValidUntil,
                proposed.Rate,
                Unit = proposed.Unit.ToString()
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminParkingTariffCreateResult(
            AdminParkingTariffCreateOutcome.Created,
            ToAdminParkingTariffSummary(proposed));
    }

    public async Task<AdminCostReport> GetCostReportAsync(
        Guid actorUserId,
        DateTimeOffset reportFrom,
        DateTimeOffset reportTo,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (reportTo <= reportFrom)
            throw new ArgumentException("To must be after from.", nameof(reportTo));

        var rows = await (
            from visit in dbContext.Visits.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on visit.UserId equals user.Id
            join vehicle in dbContext.Vehicles.AsNoTracking() on visit.VehicleId equals vehicle.Id
            where visit.Status == VisitStatus.Completed &&
                  visit.ActualEndAt.HasValue &&
                  ((visit.StartAt < reportTo && visit.ActualEndAt.Value > reportFrom) ||
                   dbContext.ProviderParkingActions.Any(action =>
                       action.VisitId == visit.Id &&
                       action.ActualStartAt.HasValue &&
                       action.ActualEndAt.HasValue &&
                       action.ActualStartAt.Value < reportTo &&
                       action.ActualEndAt.Value > reportFrom))
            orderby visit.StartAt descending
            select new
            {
                Visit = visit,
                user.Username,
                vehicle.LicensePlate
            })
            .ToListAsync(cancellationToken);

        var visitIds = rows.Select(x => x.Visit.Id).ToArray();
        var actionsByVisitId = (await dbContext.ProviderParkingActions.AsNoTracking()
                .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.VisitId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var ruleSets = await LoadRuleSetsAsync(reportFrom, reportTo, cancellationToken);
        var tariffs = await dbContext.ParkingTariffs.AsNoTracking()
            .Where(x => x.ValidFrom < reportTo && (x.ValidUntil == null || x.ValidUntil > reportFrom))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        var visitCosts = new List<AdminVisitCostSummary>(rows.Count);
        foreach (var row in rows)
        {
            var actualEnd = row.Visit.ActualEndAt!.Value;
            var visitRuleSets = ruleSets
                .Where(x => x.ProviderProductId == row.Visit.ProviderProductId)
                .ToArray();
            var visitTariffs = tariffs
                .Where(x => x.ProviderProductId == row.Visit.ProviderProductId)
                .ToArray();
            var visitActions = actionsByVisitId.GetValueOrDefault(row.Visit.Id) ?? [];
            int? paidDurationMinutes = null;

            try
            {
                var actionPaidSegments = visitActions
                    .Select(action => ProviderActionPaidTimeCalculator.CalculatePaidSegments(
                        [action], reportFrom, reportTo, visitRuleSets))
                    .ToArray();

                var paidDuration = actionPaidSegments.SelectMany(x => x).Aggregate(
                    TimeSpan.Zero,
                    (total, segment) => total + (segment.End - segment.Start));
                paidDurationMinutes = (int)Math.Floor(paidDuration.TotalMinutes);

                decimal amount = 0m;
                for (var index = 0; index < visitActions.Length; index++)
                {
                    var action = visitActions[index];
                    if (action.ProviderCostAmount is decimal providerCost &&
                        action.ActualStartAt is DateTimeOffset actionStart &&
                        action.ActualEndAt is DateTimeOffset actionEnd &&
                        actionStart >= reportFrom && actionEnd <= reportTo)
                    {
                        amount += providerCost;
                        continue;
                    }

                    amount += ProviderActionCostCalculator.Calculate(actionPaidSegments[index], visitTariffs);
                }

                var historyIncomplete = visitActions.Any(
                    x => x.HistoryStatus == ProviderHistoryStatus.Incomplete);
                visitCosts.Add(new AdminVisitCostSummary(
                    row.Visit.Id,
                    row.Visit.UserId,
                    row.Username,
                    row.LicensePlate,
                    row.Visit.StartAt,
                    actualEnd,
                    paidDurationMinutes,
                    amount,
                    !historyIncomplete,
                    historyIncomplete ? "Provider action history is incomplete." : null));
            }
            catch (InvalidOperationException exception)
            {
                visitCosts.Add(new AdminVisitCostSummary(
                    row.Visit.Id,
                    row.Visit.UserId,
                    row.Username,
                    row.LicensePlate,
                    row.Visit.StartAt,
                    actualEnd,
                    paidDurationMinutes,
                    null,
                    false,
                    exception.Message));
            }
        }

        var isComplete = visitCosts.All(x => x.IsComplete);
        var hasCompletePaidDurations = visitCosts.All(x => x.PaidDurationMinutes.HasValue);
        return new AdminCostReport(
            reportFrom,
            reportTo,
            hasCompletePaidDurations ? visitCosts.Sum(x => x.PaidDurationMinutes!.Value) : null,
            isComplete ? visitCosts.Sum(x => x.Amount!.Value) : null,
            isComplete,
            visitCosts);
    }

    public async Task<AdminUsageAnalysis> GetUsageAnalysisAsync(
        Guid actorUserId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);
        if (to <= from)
            throw new ArgumentException("To must be after from.", nameof(to));

        var report = await GetCostReportAsync(actorUserId, from, to, cancellationToken);
        var userIds = report.Visits.Select(x => x.UserId).Distinct().ToArray();

        var activeUsers = await dbContext.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.IsActive, cancellationToken);

        var plates = report.Visits.Select(x => x.LicensePlate).Distinct().ToArray();
        var plateStates = await dbContext.Vehicles.AsNoTracking()
            .Where(x => plates.Contains(x.LicensePlate))
            .Select(x => new { x.LicensePlate, x.IsActive })
            .ToListAsync(cancellationToken);
        var activePlates = plateStates
            .GroupBy(x => x.LicensePlate)
            .ToDictionary(x => x.Key, x => x.Any(vehicle => vehicle.IsActive));

        var references = report.Visits
            .Select(x => new AdminAnalysisVisitReference(
                x.VisitId,
                x.UserId,
                x.Username,
                x.LicensePlate,
                x.StartAt,
                x.ActualEndAt,
                x.PaidDurationMinutes,
                x.Amount,
                x.IsComplete))
            .ToArray();

        var byUser = references
            .GroupBy(x => new { x.UserId, x.Username })
            .Select(group => CreateAnalysisGroup(
                group.Key.UserId.ToString(),
                group.Key.UserId,
                group.Key.Username,
                !activeUsers.GetValueOrDefault(group.Key.UserId),
                group))
            .OrderByDescending(x => x.PaidDurationMinutes ?? -1)
            .ThenBy(x => x.Label)
            .ToArray();

        var byLicensePlate = references
            .GroupBy(x => x.LicensePlate)
            .Select(group => CreateAnalysisGroup(
                group.Key,
                null,
                group.Key,
                !activePlates.GetValueOrDefault(group.Key),
                group))
            .OrderByDescending(x => x.PaidDurationMinutes ?? -1)
            .ThenBy(x => x.Label)
            .ToArray();

        return new AdminUsageAnalysis(from, to, byUser, byLicensePlate);
    }

    private static AdminUsageAnalysisGroup CreateAnalysisGroup(
        string key,
        Guid? userId,
        string label,
        bool isArchived,
        IEnumerable<AdminAnalysisVisitReference> visits)
    {
        var rows = visits.OrderByDescending(x => x.StartAt).ToArray();
        var paidDurationComplete = rows.All(x => x.PaidDurationMinutes.HasValue);
        var amountComplete = rows.All(x => x.Amount.HasValue);
        var complete = rows.All(x => x.IsComplete);

        return new AdminUsageAnalysisGroup(
            key,
            userId,
            label,
            isArchived,
            rows.Length,
            paidDurationComplete ? rows.Sum(x => x.PaidDurationMinutes!.Value) : null,
            amountComplete ? rows.Sum(x => x.Amount!.Value) : null,
            complete,
            rows);
    }

    private static AdminBudgetPeriodSummary ToAdminBudgetPeriodSummary(ParkingBudgetPeriod period) =>
        new(
            period.Id,
            period.ProviderProductId,
            period.ValidFrom,
            period.ValidUntil,
            (int)period.MaximumPaidDuration.TotalMinutes);

    private static AdminParkingTariffSummary ToAdminParkingTariffSummary(ParkingTariff tariff) =>
        new(
            tariff.Id,
            tariff.ProviderProductId,
            tariff.ValidFrom,
            tariff.ValidUntil,
            tariff.Rate,
            tariff.Unit);

    public async Task<IReadOnlyList<AdminVisitSummary>> GetVisitsAsync(
        Guid actorUserId,
        Guid? userId,
        string? licensePlate,
        DateTimeOffset? from,
        DateTimeOffset? to,
        VisitStatus? status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var query =
            from visit in dbContext.Visits.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on visit.UserId equals user.Id
            join vehicle in dbContext.Vehicles.AsNoTracking() on visit.VehicleId equals vehicle.Id
            join startedBy in dbContext.Users.AsNoTracking() on visit.StartedByUserId equals startedBy.Id
            select new
            {
                Visit = visit,
                user.Username,
                Vehicle = vehicle,
                StartedByUsername = startedBy.Username
            };

        if (userId.HasValue)
            query = query.Where(x => x.Visit.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(licensePlate))
        {
            var normalizedPlate = Vehicle.NormalizeLicensePlate(licensePlate);
            query = query.Where(x => x.Vehicle.NormalizedLicensePlate.Contains(normalizedPlate));
        }

        if (from.HasValue)
            query = query.Where(x => x.Visit.StartAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.Visit.StartAt <= to.Value);

        if (status.HasValue)
            query = query.Where(x => x.Visit.Status == status.Value);

        var rows = await query
            .OrderByDescending(x => x.Visit.StartAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return Array.Empty<AdminVisitSummary>();

        var visitIds = rows.Select(x => x.Visit.Id).ToArray();
        var providerActions = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId.HasValue && visitIds.Contains(x.VisitId.Value))
            .ToListAsync(cancellationToken);
        var earliestStart = rows.Min(x => x.Visit.StartAt);
        var latestEnd = rows.Max(x => x.Visit.ActualEndAt ?? x.Visit.DesiredEndAt ?? now);
        var actionStarts = providerActions
            .Where(x => x.ActualStartAt.HasValue)
            .Select(x => x.ActualStartAt!.Value)
            .ToArray();
        var actionEnds = providerActions
            .Where(x => x.ActualEndAt.HasValue)
            .Select(x => x.ActualEndAt!.Value)
            .ToArray();
        if (actionStarts.Length > 0 && actionStarts.Min() < earliestStart)
            earliestStart = actionStarts.Min();
        if (actionEnds.Length > 0 && actionEnds.Max() > latestEnd)
            latestEnd = actionEnds.Max();
        if (latestEnd <= earliestStart)
            latestEnd = earliestStart.AddSeconds(1);

        var ruleSets = await LoadRuleSetsAsync(earliestStart, latestEnd, cancellationToken);
        var actionsByVisitId = providerActions
            .Where(x => x.VisitId.HasValue)
            .GroupBy(x => x.VisitId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());

        return rows
            .Select(x => ToAdminVisitSummary(
                x.Visit,
                x.Username,
                x.Vehicle.LicensePlate,
                x.StartedByUsername,
                now,
                actionsByVisitId.GetValueOrDefault(x.Visit.Id) ?? [],
                ruleSets.Where(ruleSet => ruleSet.ProviderProductId == x.Visit.ProviderProductId).ToArray()))
            .ToArray();
    }

    public async Task<AdminVisitDetail?> GetVisitDetailAsync(
        Guid actorUserId,
        Guid visitId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(actorUserId, cancellationToken);

        var row = await (
            from visit in dbContext.Visits.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on visit.UserId equals user.Id
            join vehicle in dbContext.Vehicles.AsNoTracking() on visit.VehicleId equals vehicle.Id
            join startedBy in dbContext.Users.AsNoTracking() on visit.StartedByUserId equals startedBy.Id
            where visit.Id == visitId
            select new
            {
                Visit = visit,
                user.Username,
                vehicle.LicensePlate,
                StartedByUsername = startedBy.Username
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var providerActionEntities = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId == visitId)
            .OrderBy(x => x.PlannedStartAt)
            .ToListAsync(cancellationToken);
        var effectiveEnd = row.Visit.ActualEndAt ?? row.Visit.DesiredEndAt ?? now;
        if (effectiveEnd <= row.Visit.StartAt)
            effectiveEnd = row.Visit.StartAt.AddSeconds(1);
        var actionStarts = providerActionEntities
            .Where(x => x.ActualStartAt.HasValue)
            .Select(x => x.ActualStartAt!.Value)
            .ToArray();
        var actionEnds = providerActionEntities
            .Where(x => x.ActualEndAt.HasValue)
            .Select(x => x.ActualEndAt!.Value)
            .ToArray();
        var ruleStart = actionStarts.Length > 0 && actionStarts.Min() < row.Visit.StartAt
            ? actionStarts.Min()
            : row.Visit.StartAt;
        var ruleEnd = actionEnds.Length > 0 && actionEnds.Max() > effectiveEnd
            ? actionEnds.Max()
            : effectiveEnd;

        var ruleSets = await LoadRuleSetsForProductAsync(
            ruleStart,
            ruleEnd,
            row.Visit.ProviderProductId,
            cancellationToken);
        var visitSummary = ToAdminVisitSummary(
            row.Visit,
            row.Username,
            row.LicensePlate,
            row.StartedByUsername,
            now,
            providerActionEntities,
            ruleSets);

        var providerProductName = row.Visit.ProviderProductId is Guid providerProductId
            ? await dbContext.ParkingProviderProducts.AsNoTracking()
                .Where(x => x.Id == providerProductId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var providerActions = providerActionEntities
            .Select(x => new AdminProviderParkingActionSummary(
                x.Id,
                x.ProviderActionId,
                x.ProviderProductId,
                x.ProviderLocation,
                x.PlannedStartAt,
                x.PlannedEndAt,
                x.ActualStartAt,
                x.ActualEndAt,
                x.ProviderStatus,
                x.State,
                x.Health))
            .ToArray();

        var providerOperations = await dbContext.ProviderOperations.AsNoTracking()
            .Where(x => x.VisitId == visitId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new AdminProviderOperationSummary(
                x.Id,
                x.OperationId,
                x.ProviderParkingActionId,
                x.ParentOperationId,
                x.Type,
                x.Status,
                x.AttemptCount,
                x.LastErrorCode,
                x.CreatedAt,
                x.AttemptStartedAt,
                x.RequestedEndAt,
                x.CompletedAt))
            .ToListAsync(cancellationToken);

        var schedulerWork = await dbContext.VisitSchedulerWork.AsNoTracking()
            .Where(x => x.VisitId == visitId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new AdminVisitSchedulerWorkSummary(
                x.Id,
                x.Type,
                x.Status,
                x.DueAt,
                x.EndReason,
                x.ProviderParkingActionId,
                x.AttemptCount,
                x.CreatedAt,
                x.ClaimedAt,
                x.CompletedAt))
            .ToListAsync(cancellationToken);

        var endTimeChanges = await (
            from change in dbContext.VisitEndTimeChanges.AsNoTracking()
            join actor in dbContext.Users.AsNoTracking() on change.ActorUserId equals actor.Id
            where change.VisitId == visitId
            orderby change.CreatedAt
            select new AdminVisitEndTimeChangeSummary(
                change.Id,
                change.OperationId,
                change.ActorUserId,
                actor.Username,
                change.PreviousDesiredEndAt,
                change.RequestedDesiredEndAt,
                change.CreatedAt,
                change.Result))
            .ToListAsync(cancellationToken);

        var schedulerTimelineEvents = await dbContext.VisitSchedulerAuditEvents.AsNoTracking()
            .Where(x => x.VisitId == visitId)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.EventOrder)
            .ThenBy(x => x.Id)
            .Select(x => new AdminVisitTimelineEventSummary(
                x.Id,
                x.OccurredAt,
                x.EventOrder,
                x.SourceType,
                x.SourceId,
                x.EventType,
                x.GroupKey,
                x.AttemptNumber,
                x.ReasonCode,
                x.DetailsJson))
            .ToListAsync(cancellationToken);

        var timelineEvents = schedulerTimelineEvents
            .Concat(endTimeChanges.Select(change => new AdminVisitTimelineEventSummary(
                change.Id,
                change.CreatedAt,
                0,
                "visit_end_time_change",
                change.Id,
                change.Result switch
                {
                    VisitEndTimeChangeResult.Pending => "visit.desired_end_change_requested",
                    VisitEndTimeChangeResult.Rejected => "visit.desired_end_change_rejected",
                    VisitEndTimeChangeResult.Applied => "visit.desired_end_change_applied",
                    _ => throw new ArgumentOutOfRangeException(nameof(change.Result))
                },
                $"attempt:{change.OperationId}:0",
                null,
                change.Result switch
                {
                    VisitEndTimeChangeResult.Pending => "desired_end_change_requested",
                    VisitEndTimeChangeResult.Rejected => "desired_end_change_rejected",
                    VisitEndTimeChangeResult.Applied => "desired_end_change_applied",
                    _ => throw new ArgumentOutOfRangeException(nameof(change.Result))
                },
                JsonSerializer.Serialize(new
                {
                    change.ActorUsername,
                    change.PreviousDesiredEndAt,
                    change.RequestedDesiredEndAt,
                    result = change.Result.ToString()
                }))))
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.EventOrder)
            .ThenBy(x => x.Id)
            .ToArray();

        var relevantRuleSets = ruleSets
            .Select(x => new AdminRuleSetSummary(
                x.Id,
                x.ValidFrom,
                x.ValidUntil,
                (int)x.MaxProviderActionDuration.TotalMinutes,
                x.Continuation,
                x.PublicHolidaysAreFree))
            .ToArray();

        var policy = row.Visit.PolicySnapshot;
        var policySnapshot = new AdminVisitPolicySnapshotSummary(
            policy.MaxPaidParkingDuration is null ? null : (int)policy.MaxPaidParkingDuration.Value.TotalMinutes,
            policy.MaxVisitElapsedDuration is null ? null : (int)policy.MaxVisitElapsedDuration.Value.TotalMinutes,
            policy.AllowVisitExtension,
            policy.AllowOpenEndedVisits);

        return new AdminVisitDetail(
            visitSummary,
            providerProductName,
            row.Visit.ProviderProductExternalId,
            row.Visit.ProviderLocation,
            policySnapshot,
            providerActions,
            providerOperations,
            schedulerWork,
            endTimeChanges,
            timelineEvents,
            relevantRuleSets);
    }

    private async Task<IReadOnlyList<ParkingRuleSet>> LoadRuleSetsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken) =>
        await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ValidFrom < end && (x.ValidUntil == null || x.ValidUntil > start))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<ParkingRuleSet>> LoadRuleSetsForProductAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? providerProductId,
        CancellationToken cancellationToken) =>
        await dbContext.ParkingRuleSets.AsNoTracking()
            .Include(x => x.PaidWindows)
            .Include(x => x.CalendarExceptions)
            .Where(x => x.ProviderProductId == providerProductId &&
                        x.ValidFrom < end &&
                        (x.ValidUntil == null || x.ValidUntil > start))
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

    private static AdminVisitSummary ToAdminVisitSummary(
        Visit visit,
        string username,
        string licensePlate,
        string startedByUsername,
        DateTimeOffset now,
        IReadOnlyList<ProviderParkingAction> providerActions,
        IReadOnlyList<ParkingRuleSet> ruleSets)
    {
        int? paidDurationMinutes = null;
        var end = visit.Status == VisitStatus.Cancelled
            ? null
            : visit.ActualEndAt ?? (visit.Status == VisitStatus.Completed ? null : now);

        if (visit.Status == VisitStatus.Completed)
        {
            try
            {
                var paidDuration = providerActions
                    .SelectMany(action => ProviderActionPaidTimeCalculator.CalculatePaidSegments(
                        [action], DateTimeOffset.MinValue, DateTimeOffset.MaxValue, ruleSets))
                    .Aggregate(TimeSpan.Zero, (duration, segment) => duration + (segment.End - segment.Start));
                paidDurationMinutes = (int)Math.Floor(paidDuration.TotalMinutes);
            }
            catch (InvalidOperationException)
            {
                // Historical configuration or action data can be incomplete; keep the Visit visible for audit.
            }
        }
        else if (end.HasValue && end.Value > visit.StartAt && ruleSets.Count > 0)
        {
            try
            {
                paidDurationMinutes = (int)Math.Floor(
                    ParkingRuleSetPaidTimeCalculator
                        .Calculate(visit.StartAt, end.Value, ruleSets)
                        .TotalMinutes);
            }
            catch (InvalidOperationException)
            {
                // Historical configuration can be incomplete; the Visit remains visible for audit.
            }
        }

        return new AdminVisitSummary(
            visit.Id,
            visit.UserId,
            username,
            visit.VehicleId,
            licensePlate,
            visit.StartedByUserId,
            startedByUsername,
            visit.StartAt,
            visit.DesiredEndAt,
            visit.ActualEndAt,
            visit.Status,
            visit.Health,
            paidDurationMinutes);
    }

    private async Task EnsureAdminAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(x => x.Id == actorUserId && x.IsActive && x.Role == UserRole.Admin, cancellationToken))
            throw new UnauthorizedAccessException("Active administrator required.");
    }

    private static bool IsValidPin(string pin) => pin.Length == 6 && pin.All(char.IsAsciiDigit);
    private static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
}