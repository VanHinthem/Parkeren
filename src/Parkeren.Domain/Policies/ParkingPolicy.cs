namespace Parkeren.Domain.Policies;
public sealed class ParkingSystemSettings
{
    private ParkingSystemSettings() { }
    public ParkingSystemSettings(
        Guid id,
        int maxConcurrentVisits,
        TimeSpan? longVisitWarningAfter = null,
        bool notifyAdminOnLongVisit = true,
        TimeSpan? longVisitReminderInterval = null,
        IEnumerable<int>? budgetWarningThresholdPercentages = null)
    {
        if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits));
        ValidateLongVisitDuration(longVisitWarningAfter, nameof(longVisitWarningAfter));
        ValidateLongVisitDuration(longVisitReminderInterval, nameof(longVisitReminderInterval));
        Id = id;
        MaxConcurrentVisits = maxConcurrentVisits;
        LongVisitWarningAfter = longVisitWarningAfter;
        NotifyAdminOnLongVisit = notifyAdminOnLongVisit;
        LongVisitReminderInterval = longVisitReminderInterval;
        BudgetWarningThresholdPercentages = ValidateBudgetWarningThresholds(
            budgetWarningThresholdPercentages ?? new[] { 80, 90, 100 });
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public int MaxConcurrentVisits { get; private set; }
    public TimeSpan? LongVisitWarningAfter { get; private set; }
    public bool NotifyAdminOnLongVisit { get; private set; }
    public TimeSpan? LongVisitReminderInterval { get; private set; }
    public int[] BudgetWarningThresholdPercentages { get; private set; } = Array.Empty<int>();
    public DateTimeOffset UpdatedAt { get; private set; }
    public void SetMaxConcurrentVisits(int maxConcurrentVisits)
    {
        if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits));
        MaxConcurrentVisits = maxConcurrentVisits;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    public void SetLongVisitNotifications(
        TimeSpan? warningAfter,
        bool notifyAdmin,
        TimeSpan? reminderInterval)
    {
        ValidateLongVisitDuration(warningAfter, nameof(warningAfter));
        ValidateLongVisitDuration(reminderInterval, nameof(reminderInterval));
        LongVisitWarningAfter = warningAfter;
        NotifyAdminOnLongVisit = notifyAdmin;
        LongVisitReminderInterval = reminderInterval;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    public void SetBudgetWarningThresholdPercentages(IEnumerable<int> thresholds)
    {
        BudgetWarningThresholdPercentages = ValidateBudgetWarningThresholds(thresholds);
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    private static int[] ValidateBudgetWarningThresholds(IEnumerable<int> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var values = thresholds.OrderBy(x => x).ToArray();
        if (values.Length == 0 || values.Any(x => x <= 0 || x > 100) || values.Distinct().Count() != values.Length)
            throw new ArgumentOutOfRangeException(nameof(thresholds));
        return values;
    }
    private static void ValidateLongVisitDuration(TimeSpan? value, string parameterName)
    {
        if (value.HasValue && value.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed class DefaultParkingPolicy
{
    private DefaultParkingPolicy() { }
    public DefaultParkingPolicy(Guid id, TimeSpan? maxPaidParkingDuration, TimeSpan? maxVisitElapsedDuration, bool allowVisitExtension, bool allowOpenEndedVisits = true, int maxConcurrentVisits = 1)
    { if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits)); Id=id; MaxPaidParkingDuration=maxPaidParkingDuration; MaxVisitElapsedDuration=maxVisitElapsedDuration; AllowVisitExtension=allowVisitExtension; AllowOpenEndedVisits=allowOpenEndedVisits; MaxConcurrentVisits=maxConcurrentVisits; UpdatedAt=DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public TimeSpan? MaxPaidParkingDuration { get; private set; }
    public TimeSpan? MaxVisitElapsedDuration { get; private set; }
    public bool AllowVisitExtension { get; private set; }
    public bool AllowOpenEndedVisits { get; private set; }
    public int MaxConcurrentVisits { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
public enum PolicyDurationOverrideMode
{
    Inherit,
    Value,
    Unlimited
}

public sealed class UserPolicyOverride
{
    private UserPolicyOverride() { }

    public UserPolicyOverride(Guid userId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        UserId = userId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetOverrides(
        PolicyDurationOverrideMode maxPaidParkingDurationMode,
        TimeSpan? maxPaidParkingDuration,
        PolicyDurationOverrideMode maxVisitElapsedDurationMode,
        TimeSpan? maxVisitElapsedDuration,
        bool? allowVisitExtension,
        bool? allowOpenEndedVisits,
        int? maxConcurrentVisits)
    {
        ValidateDurationOverride(maxPaidParkingDurationMode, maxPaidParkingDuration, nameof(maxPaidParkingDuration));
        ValidateDurationOverride(maxVisitElapsedDurationMode, maxVisitElapsedDuration, nameof(maxVisitElapsedDuration));
        if (maxConcurrentVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentVisits));

        MaxPaidParkingDurationMode = maxPaidParkingDurationMode;
        MaxPaidParkingDuration = maxPaidParkingDurationMode == PolicyDurationOverrideMode.Value
            ? maxPaidParkingDuration
            : null;
        MaxVisitElapsedDurationMode = maxVisitElapsedDurationMode;
        MaxVisitElapsedDuration = maxVisitElapsedDurationMode == PolicyDurationOverrideMode.Value
            ? maxVisitElapsedDuration
            : null;
        AllowVisitExtension = allowVisitExtension;
        AllowOpenEndedVisits = allowOpenEndedVisits;
        MaxConcurrentVisits = maxConcurrentVisits;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetMaxConcurrentVisits(int? maxConcurrentVisits) =>
        SetOverrides(
            MaxPaidParkingDurationMode,
            MaxPaidParkingDuration,
            MaxVisitElapsedDurationMode,
            MaxVisitElapsedDuration,
            AllowVisitExtension,
            AllowOpenEndedVisits,
            maxConcurrentVisits);

    public bool HasAnyOverride =>
        MaxPaidParkingDurationMode != PolicyDurationOverrideMode.Inherit ||
        MaxVisitElapsedDurationMode != PolicyDurationOverrideMode.Inherit ||
        AllowVisitExtension is not null ||
        AllowOpenEndedVisits is not null ||
        MaxConcurrentVisits is not null;

    public Guid UserId { get; private set; }
    public PolicyDurationOverrideMode MaxPaidParkingDurationMode { get; private set; } = PolicyDurationOverrideMode.Inherit;
    public TimeSpan? MaxPaidParkingDuration { get; private set; }
    public PolicyDurationOverrideMode MaxVisitElapsedDurationMode { get; private set; } = PolicyDurationOverrideMode.Inherit;
    public TimeSpan? MaxVisitElapsedDuration { get; private set; }
    public bool? AllowVisitExtension { get; private set; }
    public bool? AllowOpenEndedVisits { get; private set; }
    public int? MaxConcurrentVisits { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private static void ValidateDurationOverride(
        PolicyDurationOverrideMode mode,
        TimeSpan? value,
        string parameterName)
    {
        if (mode == PolicyDurationOverrideMode.Value)
        {
            if (!value.HasValue || value.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName);
            return;
        }

        if (value.HasValue)
            throw new ArgumentException("A duration value is only valid when override mode is Value.", parameterName);
    }
}
public sealed record EffectiveParkingPolicy(TimeSpan? MaxPaidParkingDuration, TimeSpan? MaxVisitElapsedDuration, bool AllowVisitExtension, bool AllowOpenEndedVisits = true, int MaxConcurrentVisits = 1);
public static class ParkingPolicyResolver
{
    public static EffectiveParkingPolicy Resolve(DefaultParkingPolicy defaults, UserPolicyOverride? overrides) => new(
        ResolveDuration(
            defaults.MaxPaidParkingDuration,
            overrides?.MaxPaidParkingDurationMode ?? PolicyDurationOverrideMode.Inherit,
            overrides?.MaxPaidParkingDuration),
        ResolveDuration(
            defaults.MaxVisitElapsedDuration,
            overrides?.MaxVisitElapsedDurationMode ?? PolicyDurationOverrideMode.Inherit,
            overrides?.MaxVisitElapsedDuration),
        overrides?.AllowVisitExtension ?? defaults.AllowVisitExtension,
        overrides?.AllowOpenEndedVisits ?? defaults.AllowOpenEndedVisits,
        overrides?.MaxConcurrentVisits ?? defaults.MaxConcurrentVisits);

    private static TimeSpan? ResolveDuration(
        TimeSpan? defaultValue,
        PolicyDurationOverrideMode mode,
        TimeSpan? overrideValue) =>
        mode switch
        {
            PolicyDurationOverrideMode.Inherit => defaultValue,
            PolicyDurationOverrideMode.Unlimited => null,
            PolicyDurationOverrideMode.Value => overrideValue
                ?? throw new InvalidOperationException("Duration override value is missing."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}
