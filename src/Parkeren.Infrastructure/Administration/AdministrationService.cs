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
    IStartVisitOperationalContextResolver operationalContextResolver) : IAdministrationService
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
        await dbContext.SaveChangesAsync(cancellationToken);
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

        await dbContext.SaveChangesAsync(cancellationToken);
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
        await dbContext.SaveChangesAsync(cancellationToken);
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

        if (!await dbContext.Users.AnyAsync(x => x.Id == userId, cancellationToken))
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

        await dbContext.SaveChangesAsync(cancellationToken);
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

        await dbContext.SaveChangesAsync(cancellationToken);
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

        await dbContext.SaveChangesAsync(cancellationToken);

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
                policyOverride?.MaxPaidParkingDurationMode is null or PolicyDurationOverrideMode.Inherit,
            AdminDefaultPolicyField.MaxVisitElapsedDuration =>
                policyOverride?.MaxVisitElapsedDurationMode is null or PolicyDurationOverrideMode.Inherit,
            AdminDefaultPolicyField.AllowVisitExtension =>
                policyOverride?.AllowVisitExtension is null,
            AdminDefaultPolicyField.AllowOpenEndedVisits =>
                policyOverride?.AllowOpenEndedVisits is null,
            AdminDefaultPolicyField.MaxConcurrentVisits =>
                policyOverride?.MaxConcurrentVisits is null,
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

        var earliestStart = rows.Min(x => x.Visit.StartAt);
        var latestEnd = rows.Max(x => x.Visit.ActualEndAt ?? x.Visit.DesiredEndAt ?? now);
        if (latestEnd <= earliestStart)
            latestEnd = earliestStart.AddSeconds(1);

        var ruleSets = await LoadRuleSetsAsync(earliestStart, latestEnd, cancellationToken);

        return rows
            .Select(x => ToAdminVisitSummary(
                x.Visit,
                x.Username,
                x.Vehicle.LicensePlate,
                x.StartedByUsername,
                now,
                ruleSets))
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

        var effectiveEnd = row.Visit.ActualEndAt ?? row.Visit.DesiredEndAt ?? now;
        if (effectiveEnd <= row.Visit.StartAt)
            effectiveEnd = row.Visit.StartAt.AddSeconds(1);

        var ruleSets = await LoadRuleSetsAsync(row.Visit.StartAt, effectiveEnd, cancellationToken);
        var visitSummary = ToAdminVisitSummary(
            row.Visit,
            row.Username,
            row.LicensePlate,
            row.StartedByUsername,
            now,
            ruleSets);

        var providerActions = await dbContext.ProviderParkingActions.AsNoTracking()
            .Where(x => x.VisitId == visitId)
            .OrderBy(x => x.PlannedStartAt)
            .Select(x => new AdminProviderParkingActionSummary(
                x.Id,
                x.ProviderActionId,
                x.PlannedStartAt,
                x.PlannedEndAt,
                x.ActualStartAt,
                x.ActualEndAt,
                x.ProviderStatus,
                x.State,
                x.Health))
            .ToListAsync(cancellationToken);

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
            policySnapshot,
            providerActions,
            providerOperations,
            endTimeChanges,
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

    private static AdminVisitSummary ToAdminVisitSummary(
        Visit visit,
        string username,
        string licensePlate,
        string startedByUsername,
        DateTimeOffset now,
        IReadOnlyList<ParkingRuleSet> ruleSets)
    {
        int? paidDurationMinutes = null;
        var end = visit.Status == VisitStatus.Cancelled
            ? null
            : visit.ActualEndAt ?? (visit.Status == VisitStatus.Completed ? null : now);

        if (end.HasValue && end.Value > visit.StartAt && ruleSets.Count > 0)
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
