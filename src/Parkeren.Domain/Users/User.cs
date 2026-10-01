namespace Parkeren.Domain.Users;

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
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string NormalizedUsername { get; private set; } = string.Empty;
    public string PinHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void ChangePinHash(string pinHash) => PinHash = pinHash;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
