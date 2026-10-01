using Parkeren.Domain.Users;
using Parkeren.Domain.Rules;
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
    Task<AdminUserDetail?> GetUserDetailAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken);
    Task<AdminUserPolicyUpdateResult> SetUserPolicyAsync(
        Guid actorUserId,
        Guid userId,
        int? maxPaidParkingDurationMinutes,
        int? maxVisitElapsedDurationMinutes,
        bool? allowVisitExtension,
        bool? allowOpenEndedVisits,
        int? maxConcurrentVisits,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminVisitSummary>> GetVisitsAsync(
        Guid actorUserId,
        Guid? userId,
        string? licensePlate,
        DateTimeOffset? from,
        DateTimeOffset? to,
        VisitStatus? status,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AdminVisitDetail?> GetVisitDetailAsync(Guid actorUserId, Guid visitId, DateTimeOffset now, CancellationToken cancellationToken);
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

public sealed record AdminVisitSummary(
    Guid Id,
    Guid UserId,
    string Username,
    Guid VehicleId,
    string LicensePlate,
    Guid StartedByUserId,
    string StartedByUsername,
    DateTimeOffset StartAt,
    DateTimeOffset? DesiredEndAt,
    DateTimeOffset? ActualEndAt,
    VisitStatus Status,
    VisitHealth Health,
    int? PaidDurationMinutes);

public sealed record AdminVisitPolicySnapshotSummary(
    int? MaxPaidParkingDurationMinutes,
    int? MaxVisitElapsedDurationMinutes,
    bool AllowVisitExtension,
    bool AllowOpenEndedVisits);

public sealed record AdminProviderParkingActionSummary(
    Guid Id,
    string? ProviderActionId,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    string? ProviderStatus,
    ProviderActionState State,
    ProviderActionHealth Health);

public sealed record AdminProviderOperationSummary(
    Guid Id,
    Guid OperationId,
    Guid? ProviderParkingActionId,
    Guid? ParentOperationId,
    ProviderOperationType Type,
    ProviderOperationStatus Status,
    int AttemptCount,
    string? LastErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AttemptStartedAt,
    DateTimeOffset? RequestedEndAt,
    DateTimeOffset? CompletedAt);

public sealed record AdminVisitEndTimeChangeSummary(
    Guid Id,
    Guid OperationId,
    Guid ActorUserId,
    string ActorUsername,
    DateTimeOffset? PreviousDesiredEndAt,
    DateTimeOffset? RequestedDesiredEndAt,
    DateTimeOffset CreatedAt,
    VisitEndTimeChangeResult Result);

public sealed record AdminRuleSetSummary(
    Guid Id,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    int MaxProviderActionDurationMinutes,
    ProviderCoverageContinuation Continuation,
    bool PublicHolidaysAreFree);

public sealed record AdminVisitDetail(
    AdminVisitSummary Visit,
    AdminVisitPolicySnapshotSummary PolicySnapshot,
    IReadOnlyList<AdminProviderParkingActionSummary> ProviderActions,
    IReadOnlyList<AdminProviderOperationSummary> ProviderOperations,
    IReadOnlyList<AdminVisitEndTimeChangeSummary> EndTimeChanges,
    IReadOnlyList<AdminRuleSetSummary> RelevantRuleSets);

public sealed record AdminParkingPolicyValues(
    int? MaxPaidParkingDurationMinutes,
    int? MaxVisitElapsedDurationMinutes,
    bool AllowVisitExtension,
    bool AllowOpenEndedVisits,
    int MaxConcurrentVisits);

public sealed record AdminParkingPolicyOverrideValues(
    int? MaxPaidParkingDurationMinutes,
    int? MaxVisitElapsedDurationMinutes,
    bool? AllowVisitExtension,
    bool? AllowOpenEndedVisits,
    int? MaxConcurrentVisits);

public sealed record AdminUserPolicyDetail(
    AdminParkingPolicyValues Defaults,
    AdminParkingPolicyOverrideValues Overrides,
    AdminParkingPolicyValues Effective,
    int GlobalMaxConcurrentVisits);

public sealed record AdminUserDetail(
    UserSummary User,
    IReadOnlyList<VehicleSummary> AssignedVehicles,
    AdminUserPolicyDetail Policy,
    int ActiveVisitCount);

public enum AdminUserPolicyUpdateOutcome
{
    Updated,
    NotFound,
    Invalid,
    ActiveVisitConflict
}

public sealed record AdminUserPolicyUpdateResult(
    AdminUserPolicyUpdateOutcome Outcome,
    int ActiveVisitCount,
    AdminUserPolicyDetail? Policy);
