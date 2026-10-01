using Parkeren.Domain.Users;
using Parkeren.Domain.Visits;

namespace Parkeren.Application.Administration;

public interface IAdministrationService
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(Guid actorUserId, CancellationToken cancellationToken);
    Task<CreateUserResult?> CreateUserAsync(Guid actorUserId, string username, string pin, UserRole role, CancellationToken cancellationToken);
    Task<bool> SetUserActiveAsync(Guid actorUserId, Guid userId, bool isActive, CancellationToken cancellationToken);
    Task<bool> SetUserMaxConcurrentVisitsAsync(Guid actorUserId, Guid userId, int? maxConcurrentVisits, CancellationToken cancellationToken);
    Task<int> GetGlobalMaxConcurrentVisitsAsync(Guid actorUserId, CancellationToken cancellationToken);
    Task<bool> SetGlobalMaxConcurrentVisitsAsync(Guid actorUserId, int maxConcurrentVisits, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehicleSummary>> GetVehiclesAsync(Guid actorUserId, CancellationToken cancellationToken);
    Task<VehicleSummary?> CreateVehicleAsync(Guid actorUserId, string licensePlate, string? displayName, CancellationToken cancellationToken);
    Task<bool> SetVehicleActiveAsync(Guid actorUserId, Guid vehicleId, bool isActive, CancellationToken cancellationToken);
    Task<bool> AssignVehicleAsync(Guid actorUserId, Guid userId, Guid vehicleId, CancellationToken cancellationToken);
    Task<bool> UnassignVehicleAsync(Guid actorUserId, Guid userId, Guid vehicleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehicleSummary>?> GetAuthorizedVehiclesAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehicleSummary>?> GetAssignedVehiclesAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken);
    Task<AdminDashboardSummary> GetDashboardAsync(Guid actorUserId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AdminParkingPolicySummary?> GetUserParkingPolicyAsync(Guid actorUserId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record UserSummary(Guid Id, string Username, UserRole Role, bool IsActive, int? MaxConcurrentVisits);
public sealed record CreateUserResult(Guid Id, string Username, UserRole Role, bool IsActive);
public sealed record VehicleSummary(Guid Id, string LicensePlate, string? DisplayName, bool IsActive);

public sealed record AdminDashboardSummary(
    int Used,
    int Total,
    IReadOnlyList<AdminActiveVisitSummary> ActiveVisits);

public sealed record AdminActiveVisitSummary(
    Guid Id,
    Guid UserId,
    string Username,
    Guid VehicleId,
    string LicensePlate,
    DateTimeOffset StartAt,
    DateTimeOffset? DesiredEndAt,
    VisitStatus Status,
    VisitHealth Health,
    int? PaidDurationMinutes);

public sealed record AdminParkingPolicySummary(
    int? MaxPaidParkingDurationMinutes,
    int? MaxVisitElapsedDurationMinutes,
    bool AllowVisitExtension,
    bool AllowOpenEndedVisits,
    int MaxConcurrentVisits);
