using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Administration;
using Parkeren.Application.Authentication;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Authentication;

internal sealed class AuthenticationService(
    ParkerenDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IAdminAuditWriter auditWriter) : IAuthenticationService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public async Task<LoginResult?> LoginAsync(string username, string pin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || !IsValidPin(pin))
            return null;

        var normalized = NormalizeUsername(username);
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
            return null;

        var verification = passwordHasher.VerifyHashedPassword(user, user.PinHash, pin);
        if (verification == PasswordVerificationResult.Failed)
            return null;

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.ChangePinHash(passwordHasher.HashPassword(user, pin));

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(SessionLifetime);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        dbContext.UserSessions.Add(new UserSession(Guid.NewGuid(), user.Id, HashToken(token), now, expiresAt));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(token, expiresAt, ToAuthenticatedUser(user));
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string sessionToken, CancellationToken cancellationToken)
    {
        var session = await GetValidSessionAsync(sessionToken, cancellationToken);
        return session is null ? null : ToAuthenticatedUser(session.User);
    }

    public async Task<bool> ChangePinAsync(
        string sessionToken,
        string currentPin,
        string newPin,
        CancellationToken cancellationToken)
    {
        if (!IsValidPin(currentPin) || !IsValidPin(newPin))
            return false;

        var session = await GetValidSessionAsync(sessionToken, cancellationToken);
        if (session is null)
            return false;

        var verification = passwordHasher.VerifyHashedPassword(session.User, session.User.PinHash, currentPin);
        if (verification == PasswordVerificationResult.Failed)
            return false;

        session.User.ChangePinHash(passwordHasher.HashPassword(session.User, newPin));
        await RevokeOtherSessionsAsync(session.UserId, session.Id, DateTimeOffset.UtcNow, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ResetPinAsync(
        Guid actorUserId,
        Guid userId,
        string newPin,
        CancellationToken cancellationToken)
    {
        if (!IsValidPin(newPin) || !await IsActiveAdminAsync(actorUserId, cancellationToken))
            return false;

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return false;

        user.ChangePinHash(passwordHasher.HashPassword(user, newPin));
        await RevokeSessionsAsync(userId, DateTimeOffset.UtcNow, cancellationToken);
        await auditWriter.WriteAsync(
            actorUserId,
            "UserPinReset",
            "User",
            user.Id.ToString(),
            new { user.Username },
            cancellationToken);
        return true;
    }

    public async Task<bool> RevokeAllSessionsAsync(
        Guid actorUserId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await IsActiveAdminAsync(actorUserId, cancellationToken))
            return false;

        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return false;

        await RevokeSessionsAsync(userId, DateTimeOffset.UtcNow, cancellationToken);
        await auditWriter.WriteAsync(
            actorUserId,
            "UserSessionsRevoked",
            "User",
            user.Id.ToString(),
            new { user.Username },
            cancellationToken);
        return true;
    }

    public async Task LogoutAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return;

        var tokenHash = HashToken(sessionToken);
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (session is null)
            return;

        session.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<UserSession?> GetValidSessionAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        var tokenHash = HashToken(sessionToken);
        var now = DateTimeOffset.UtcNow;
        var session = await dbContext.UserSessions
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        return session is not null && session.IsValidAt(now) && session.User.Status == UserStatus.Active ? session : null;
    }

    private Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(x => x.Id == userId && x.Status == UserStatus.Active && x.Role == UserRole.Admin, cancellationToken);

    private async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
            session.Revoke(now);
    }

    private async Task RevokeOtherSessionsAsync(
        Guid userId,
        Guid currentSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions
            .Where(x => x.UserId == userId && x.Id != currentSessionId && x.RevokedAt == null && x.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
            session.Revoke(now);
    }

    private static AuthenticatedUser ToAuthenticatedUser(User user) => new(user.Id, user.Username, user.Role);
    private static bool IsValidPin(string pin) => pin.Length == 6 && pin.All(char.IsAsciiDigit);
    internal static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
