using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Parkeren.Application.Authentication;
using Parkeren.Domain.Users;
using Parkeren.Infrastructure.Persistence;

namespace Parkeren.Infrastructure.Authentication;

internal sealed class AuthenticationService(
    ParkerenDbContext dbContext,
    IPasswordHasher<User> passwordHasher) : IAuthenticationService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public async Task<LoginResult?> LoginAsync(string username, string pin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || !IsValidPin(pin))
            return null;

        var normalized = NormalizeUsername(username);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            x => x.NormalizedUsername == normalized,
            cancellationToken);

        if (user is null || !user.IsActive)
            return null;

        var verification = passwordHasher.VerifyHashedPassword(user, user.PinHash, pin);
        if (verification == PasswordVerificationResult.Failed)
            return null;

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePinHash(passwordHasher.HashPassword(user, pin));
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(SessionLifetime);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        dbContext.UserSessions.Add(new UserSession(Guid.NewGuid(), user.Id, HashToken(token), now, expiresAt));
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult(token, expiresAt, new AuthenticatedUser(user.Id, user.Username, user.Role));
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        var tokenHash = HashToken(sessionToken);
        var now = DateTimeOffset.UtcNow;
        var session = await dbContext.UserSessions
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (session is null || !session.IsValidAt(now) || !session.User.IsActive)
            return null;

        return new AuthenticatedUser(session.User.Id, session.User.Username, session.User.Role);
    }

    public async Task LogoutAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return;

        var tokenHash = HashToken(sessionToken);
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            x => x.TokenHash == tokenHash,
            cancellationToken);

        if (session is null)
            return;

        session.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsValidPin(string pin) => pin.Length == 6 && pin.All(char.IsAsciiDigit);
    internal static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
