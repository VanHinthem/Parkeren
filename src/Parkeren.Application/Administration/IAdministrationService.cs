using Parkeren.Domain.Users;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Policies;
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
        PolicyDurationOverrideMode maxPaidParkingDurationMode,
        int? maxPaidParkingDurationMinutes,
        PolicyDurationOverrideMode maxVisitElapsedDurationMode,
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
    Task<AdminSystemSettingsSummary> GetSystemSettingsAsync(Guid actorUserId, CancellationToken cancellationToken);
    Task<AdminDefaultPolicyUpdateResult> SetDefaultParkingPolicyAsync(
        Guid actorUserId,
        int? maxPaidParkingDurationMinutes,
        int? maxVisitElapsedDurationMinutes,
        bool allowVisitExtension,
        bool allowOpenEndedVisits,
        int maxConcurrentVisits,
        CancellationToken cancellationToken);
    Task<AdminWarningSettingsUpdateResult> SetWarningSettingsAsync(
        Guid actorUserId,
        int? longVisitWarningAfterMinutes,
        bool notifyAdminOnLongVisit,
        int? longVisitReminderIntervalMinutes,
        IReadOnlyList<int> budgetWarningThresholdPercentages,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminParkingRuleSetVersion>> GetParkingRuleSetsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminParkingRuleSetVersion>> GetParkingRuleSetsForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        CancellationToken cancellationToken);
    Task<AdminParkingRuleSetCreateResult> CreateParkingRuleSetVersionAsync(
        Guid actorUserId,
        DateTimeOffset validFrom,
        int maxProviderActionDurationMinutes,
        ProviderCoverageContinuation continuation,
        bool publicHolidaysAreFree,
        IReadOnlyList<AdminPaidWindowInput> paidWindows,
        IReadOnlyList<AdminCalendarExceptionInput> calendarExceptions,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AdminParkingRuleSetCreateResult> CreateParkingRuleSetVersionForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        DateTimeOffset validFrom,
        int maxProviderActionDurationMinutes,
        ProviderCoverageContinuation continuation,
        bool publicHolidaysAreFree,
        IReadOnlyList<AdminPaidWindowInput> paidWindows,
        IReadOnlyList<AdminCalendarExceptionInput> calendarExceptions,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminBudgetPeriodSummary>> GetBudgetPeriodsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminBudgetPeriodSummary>> GetBudgetPeriodsForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        CancellationToken cancellationToken);
    Task<AdminBudgetPeriodCreateResult> CreateBudgetPeriodAsync(
        Guid actorUserId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        int maximumPaidDurationMinutes,
        CancellationToken cancellationToken);
    Task<AdminBudgetPeriodCreateResult> CreateBudgetPeriodForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        int maximumPaidDurationMinutes,
        CancellationToken cancellationToken);
    Task<AdminBudgetUsageSummary?> GetBudgetUsageAsync(
        Guid actorUserId,
        Guid? budgetPeriodId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminParkingTariffSummary>> GetParkingTariffsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminParkingTariffSummary>> GetParkingTariffsForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        CancellationToken cancellationToken);
    Task<AdminParkingTariffCreateResult> CreateParkingTariffAsync(
        Guid actorUserId,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        decimal rate,
        ParkingTariffUnit unit,
        CancellationToken cancellationToken);
    Task<AdminParkingTariffCreateResult> CreateParkingTariffForProductAsync(
        Guid actorUserId,
        Guid providerProductId,
        DateTimeOffset validFrom,
        DateTimeOffset? validUntil,
        decimal rate,
        ParkingTariffUnit unit,
        CancellationToken cancellationToken);
    Task<AdminCostReport> GetCostReportAsync(
        Guid actorUserId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
    Task<AdminUsageAnalysis> GetUsageAnalysisAsync(
        Guid actorUserId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
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
    string? ProviderProductId,
    string? ProviderLocation,
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
    string? ProviderProductName,
    string? ProviderProductExternalId,
    string? ProviderLocation,
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
    PolicyDurationOverrideMode MaxPaidParkingDurationMode,
    int? MaxPaidParkingDurationMinutes,
    PolicyDurationOverrideMode MaxVisitElapsedDurationMode,
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

public sealed record AdminSystemSettingsSummary(
    AdminParkingPolicyValues DefaultPolicy,
    DateTimeOffset DefaultPolicyUpdatedAt,
    int GlobalMaxConcurrentVisits,
    int? LongVisitWarningAfterMinutes,
    bool NotifyAdminOnLongVisit,
    int? LongVisitReminderIntervalMinutes,
    IReadOnlyList<int> BudgetWarningThresholdPercentages,
    DateTimeOffset SystemSettingsUpdatedAt);

public enum AdminDefaultPolicyField
{
    MaxPaidParkingDuration,
    MaxVisitElapsedDuration,
    AllowVisitExtension,
    AllowOpenEndedVisits,
    MaxConcurrentVisits
}

public enum AdminDefaultPolicyUpdateOutcome
{
    Updated,
    Invalid,
    ActiveVisitConflict
}

public sealed record AdminDefaultPolicyUpdateResult(
    AdminDefaultPolicyUpdateOutcome Outcome,
    int AffectedActiveVisitCount,
    IReadOnlyList<AdminDefaultPolicyField> BlockedFields,
    AdminParkingPolicyValues CurrentPolicy);

public enum AdminWarningSettingsUpdateOutcome
{
    Updated,
    Invalid
}

public sealed record AdminWarningSettingsUpdateResult(
    AdminWarningSettingsUpdateOutcome Outcome,
    AdminSystemSettingsSummary Settings);

public sealed record AdminPaidWindowInput(
    DayOfWeek Day,
    TimeOnly Start,
    TimeOnly End);

public sealed record AdminCalendarExceptionInput(
    DateOnly Date,
    bool IsPaid);

public sealed record AdminPaidWindowSummary(
    Guid Id,
    DayOfWeek Day,
    TimeOnly Start,
    TimeOnly End);

public sealed record AdminCalendarExceptionSummary(
    Guid Id,
    DateOnly Date,
    bool IsPaid);

public sealed record AdminParkingRuleSetVersion(
    Guid Id,
    Guid? ProviderProductId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    int MaxProviderActionDurationMinutes,
    ProviderCoverageContinuation Continuation,
    bool PublicHolidaysAreFree,
    IReadOnlyList<AdminPaidWindowSummary> PaidWindows,
    IReadOnlyList<AdminCalendarExceptionSummary> CalendarExceptions);

public enum AdminParkingRuleSetCreateOutcome
{
    Created,
    Invalid,
    MustBeFuture,
    SequenceConflict
}

public sealed record AdminParkingRuleSetCreateResult(
    AdminParkingRuleSetCreateOutcome Outcome,
    AdminParkingRuleSetVersion? Version);

public sealed record AdminBudgetPeriodSummary(
    Guid Id,
    Guid? ProviderProductId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    int MaximumPaidDurationMinutes);

public enum AdminBudgetPeriodCreateOutcome
{
    Created,
    Invalid,
    Overlap
}

public sealed record AdminBudgetPeriodCreateResult(
    AdminBudgetPeriodCreateOutcome Outcome,
    AdminBudgetPeriodSummary? Period);

public sealed record AdminBudgetUsageSummary(
    AdminBudgetPeriodSummary Period,
    int? UsedPaidDurationMinutes,
    int? RemainingPaidDurationMinutes,
    bool IsComplete,
    string? Error);

public sealed record AdminParkingTariffSummary(
    Guid Id,
    Guid? ProviderProductId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil,
    decimal Rate,
    ParkingTariffUnit Unit);

public enum AdminParkingTariffCreateOutcome
{
    Created,
    Invalid,
    Overlap
}

public sealed record AdminParkingTariffCreateResult(
    AdminParkingTariffCreateOutcome Outcome,
    AdminParkingTariffSummary? Tariff);

public sealed record AdminVisitCostSummary(
    Guid VisitId,
    Guid UserId,
    string Username,
    string LicensePlate,
    DateTimeOffset StartAt,
    DateTimeOffset ActualEndAt,
    int? PaidDurationMinutes,
    decimal? Amount,
    bool IsComplete,
    string? Error);

public sealed record AdminCostReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int? TotalPaidDurationMinutes,
    decimal? TotalAmount,
    bool IsComplete,
    IReadOnlyList<AdminVisitCostSummary> Visits);

public sealed record AdminAnalysisVisitReference(
    Guid VisitId,
    Guid UserId,
    string Username,
    string LicensePlate,
    DateTimeOffset StartAt,
    DateTimeOffset ActualEndAt,
    int? PaidDurationMinutes,
    decimal? Amount,
    bool IsComplete);

public sealed record AdminUsageAnalysisGroup(
    string Key,
    Guid? UserId,
    string Label,
    bool IsArchived,
    int VisitCount,
    int? PaidDurationMinutes,
    decimal? Amount,
    bool IsComplete,
    IReadOnlyList<AdminAnalysisVisitReference> Visits);

public sealed record AdminUsageAnalysis(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<AdminUsageAnalysisGroup> ByUser,
    IReadOnlyList<AdminUsageAnalysisGroup> ByLicensePlate);
