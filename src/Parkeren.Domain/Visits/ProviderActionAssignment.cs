namespace Parkeren.Domain.Visits;

public enum ProviderActionOrigin
{
    Managed,
    Imported,
    External
}

public enum ProviderActionAssignmentSource
{
    Unassigned,
    Confirmed,
    Inferred,
    ManuallyAssigned
}

/// <summary>
/// User attribution of a provider action, independent of its owning Visit.
/// </summary>
public sealed record ProviderActionAssignment(Guid? UserId, ProviderActionAssignmentSource Source)
{
    public static ProviderActionAssignment Unassigned { get; } =
        new(null, ProviderActionAssignmentSource.Unassigned);

    public static ProviderActionAssignment Confirmed(Guid userId) =>
        ForUser(userId, ProviderActionAssignmentSource.Confirmed);

    public static ProviderActionAssignment Manual(Guid userId) =>
        ForUser(userId, ProviderActionAssignmentSource.ManuallyAssigned);

    public static ProviderActionAssignment InferFromVehicleUsers(IEnumerable<Guid> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var distinctUsers = userIds.Distinct().Take(2).ToArray();
        return distinctUsers.Length == 1
            ? ForUser(distinctUsers[0], ProviderActionAssignmentSource.Inferred)
            : Unassigned;
    }

    public ProviderActionAssignment PreserveDuringHistorySync() => this;

    private static ProviderActionAssignment ForUser(Guid userId, ProviderActionAssignmentSource source)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A non-empty user id is required.", nameof(userId));

        return new ProviderActionAssignment(userId, source);
    }
}
