namespace Parkeren.Domain.Users;

public enum UserStatus
{
    Active,
    Inactive,
    Archived
}

public sealed class User
{
    private User() { }

    public User(Guid id, string username, string normalizedUsername, string pinHash, UserRole role)
    {
        Id = id;
        Username = username;
        NormalizedUsername = normalizedUsername;
        PinHash = pinHash;
        Role = role;
        Status = UserStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string NormalizedUsername { get; private set; } = string.Empty;
    public string PinHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public bool IsActive => Status == UserStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }

    public void ChangePinHash(string pinHash) => PinHash = pinHash;
    public void Deactivate()
    {
        if (Status != UserStatus.Archived)
            Status = UserStatus.Inactive;
    }

    public void Activate()
    {
        if (Status == UserStatus.Archived)
            throw new InvalidOperationException("Archived users cannot be activated.");

        Status = UserStatus.Active;
    }

    public void Archive() => Status = UserStatus.Archived;
}
