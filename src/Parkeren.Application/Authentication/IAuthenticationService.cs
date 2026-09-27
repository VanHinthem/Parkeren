using Parkeren.Domain.Users;

namespace Parkeren.Application.Authentication;

public interface IAuthenticationService
{
    Task<LoginResult?> LoginAsync(string username, string pin, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> AuthenticateAsync(string sessionToken, CancellationToken cancellationToken);
    Task<bool> ChangePinAsync(string sessionToken, string currentPin, string newPin, CancellationToken cancellationToken);
    Task<bool> ResetPinAsync(Guid actorUserId, Guid userId, string newPin, CancellationToken cancellationToken);
    Task<bool> RevokeAllSessionsAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken);
    Task LogoutAsync(string sessionToken, CancellationToken cancellationToken);
}

public sealed record LoginResult(string SessionToken, DateTimeOffset ExpiresAt, AuthenticatedUser User);
public sealed record AuthenticatedUser(Guid Id, string Username, UserRole Role);
