using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.User.Services;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>Persists independently rotating, hashed refresh credentials.</summary>
public class RefreshSessionService : IRefreshSessionService
{
    private readonly ApplicationDbContext _context;
    private readonly ITokenService _tokens;

    public RefreshSessionService(ApplicationDbContext context, ITokenService tokens)
    {
        _context = context;
        _tokens = tokens;
    }

    public Task<string> IssueAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        WithLockedUserAsync(user, () => IssueUnderLockAsync(user, cancellationToken), cancellationToken);

    public Task<string?> RotateAsync(ApplicationUser user, string refreshToken, CancellationToken cancellationToken) =>
        WithLockedUserAsync(user, () => RotateUnderLockAsync(user, refreshToken, cancellationToken), cancellationToken);

    public async Task RevokeAllAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        await WithLockedUserAsync(user, () => RevokeUnderLockAsync(user, cancellationToken), cancellationToken);

    private async Task<T> WithLockedUserAsync<T>(ApplicationUser user, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        // Never let a credential save accidentally publish unrelated pending account work.
        if (_context.ChangeTracker.HasChanges())
            throw new ConflictException("The account changed. Sign in again.");
        await using var transaction = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
        var completed = false;
        try
        {
            await CustomerAccountWriteGuard.AcquireAsync(_context, user.Id, false, cancellationToken);
            var current = await _context.Users.AsNoTracking().SingleAsync(row => row.Id == user.Id, cancellationToken);
            // A login/refresh may have validated before reset, role change or erasure won the lock.
            // Revalidate its original account authority; never refresh the stale caller into it.
            if (current.IsDeleted || current.SecurityStamp != user.SecurityStamp || current.Role != user.Role
                || current.Email != user.Email || current.EmailConfirmed != user.EmailConfirmed
                || current.RefreshToken != user.RefreshToken || current.RefreshTokenExpiryTime != user.RefreshTokenExpiryTime)
                throw new UnauthorizedAccessException("The account changed. Sign in again.");
            // Identity resolution cannot turn a preloaded credential into current authority.
            foreach (var entry in _context.ChangeTracker.Entries<RefreshSession>().Where(entry => entry.Entity.UserId == user.Id).ToArray())
                await entry.ReloadAsync(cancellationToken);
            var result = await work();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            completed = true;
            return result;
        }
        finally
        {
            // An outer account transaction owns rollback/detach. Never clear its unrelated state.
            if (!completed && transaction is not null) _context.ChangeTracker.Clear();
        }
    }

    private async Task<string> IssueUnderLockAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var rawToken = _tokens.GenerateRefreshToken();
        _context.RefreshSessions.Add(NewSession(user, rawToken));
        await _context.SaveChangesAsync(cancellationToken);
        return rawToken;
    }

    private async Task<string?> RotateUnderLockAsync(
        ApplicationUser user, string refreshToken, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // Do not query by hash: the comparison itself must be fixed-time. Reading this user's
        // small session set also avoids turning the token hash into a database comparison oracle.
        var sessions = await _context.RefreshSessions
            .Where(session => session.UserId == user.Id)
            .ToListAsync(cancellationToken);

        RefreshSession? matchedSession = null;
        foreach (var session in sessions)
        {
            if (_tokens.IsRefreshTokenHashMatch(session.TokenHash, refreshToken))
            {
                matchedSession ??= session;
            }
        }

        if (matchedSession is not null)
        {
            if (!matchedSession.IsUsableAt(now))
            {
                return null;
            }

            var replacement = _tokens.GenerateRefreshToken();
            matchedSession.TokenHash = _tokens.HashRefreshToken(replacement);
            matchedSession.ExpiresAt = _tokens.GetRefreshTokenExpiration();
            matchedSession.UpdatedAt = now;
            matchedSession.UpdatedBy = user.Id.ToString();
            await _context.SaveChangesAsync(cancellationToken);
            return replacement;
        }

        // Compatibility bridge: deployments before this migration stored exactly one hash on
        // ApplicationUser. Accept a still-live legacy credential once, rotate it into the new
        // table, then erase the legacy copy so an old token cannot remain usable indefinitely.
        if (user.RefreshTokenExpiryTime > now &&
            _tokens.IsRefreshTokenHashMatch(user.RefreshToken, refreshToken))
        {
            var replacement = _tokens.GenerateRefreshToken();
            _context.RefreshSessions.Add(NewSession(user, replacement));
            user.RefreshToken = string.Empty;
            user.RefreshTokenExpiryTime = now;
            user.UpdatedAt = now;
            user.UpdatedBy = user.Id.ToString();
            await _context.SaveChangesAsync(cancellationToken);
            return replacement;
        }

        return null;
    }

    private async Task<bool> RevokeUnderLockAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sessions = await _context.RefreshSessions
            .Where(session => session.UserId == user.Id && session.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.UpdatedAt = now;
            session.UpdatedBy = user.Id.ToString();
        }

        user.RefreshToken = string.Empty;
        user.RefreshTokenExpiryTime = now;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private RefreshSession NewSession(ApplicationUser user, string rawToken) => new()
    {
        UserId = user.Id,
        TokenHash = _tokens.HashRefreshToken(rawToken),
        ExpiresAt = _tokens.GetRefreshTokenExpiration(),
        CreatedAt = DateTime.UtcNow,
        CreatedBy = user.Id.ToString()
    };
}
