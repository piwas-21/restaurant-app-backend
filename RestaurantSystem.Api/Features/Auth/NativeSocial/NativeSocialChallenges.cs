using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Creates expiring challenges; completion must lock and consume the stored original once.</summary>
public sealed class NativeSocialChallenges(ApplicationDbContext context, TimeProvider time,
    IOptions<CustomerNativeSocialSettings> settings,
    INativeSocialAccess access, UserManager<ApplicationUser> users) : INativeSocialChallenges
{
    private const int CleanupBatchSize = 100;
    private const int MaximumActiveChallenges = 1000;

    public async Task<NativeSocialChallengeDto> CreateAsync(string provider, Guid? owner, string? currentPassword, CancellationToken cancellationToken)
    {
        var tenantId = await access.RequireEnabledAsync(provider, cancellationToken);
        var password = currentPassword ?? string.Empty;
        if (owner is null && currentPassword is not null || owner is not null
            && (password.Length == 0 || password.Length > 1000))
            throw new BadRequestException("Account reauthentication is required.");
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null)
            throw new ConflictException("The account changed. Start sign-in again.");
        var proof = NativeSocialChallengeProof.Create();
        var challenge = new CustomerSocialChallenge
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            Flow = owner is null ? "login" : "link",
            UserId = owner,
            ProofHash = NativeSocialChallengeProof.Hash(proof),
            Nonce = NativeSocialChallengeProof.Create(),
        };
        // This statement commits before any owner lock. Never retain cleanup row locks while
        // waiting for User: an anonymous returning completion can already hold its challenge.
        var cutoff = time.GetUtcNow().UtcDateTime;
        await context.CustomerSocialChallenges.Where(row => row.TenantId == tenantId && row.ExpiresAt <= cutoff)
            .OrderBy(row => row.ExpiresAt).Take(CleanupBatchSize).ExecuteDeleteAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await NativeSocialLocks.PoolAsync(context, tenantId, cancellationToken);
            if (owner is { } userId)
            {
                var reauthentication = await ReauthenticateAsync(userId, password, cancellationToken);
                if (!reauthentication.Authenticated)
                {
                    // Preserve the failed-attempt counter without creating a challenge/session.
                    await transaction.CommitAsync(cancellationToken);
                    committed = true;
                    throw ReauthenticationRequired();
                }
                challenge.SecurityStampHash = reauthentication.StampHash;
            }
            var now = time.GetUtcNow().UtcDateTime;
            if (await context.CustomerSocialChallenges.CountAsync(row => row.TenantId == tenantId && row.ExpiresAt > now, cancellationToken) >= MaximumActiveChallenges)
                throw new ConflictException("Native sign-in is temporarily unavailable.");
            // Cleanup can itself wait on an expired row. Start the full lifetime only afterward.
            challenge.CreatedAt = time.GetUtcNow().UtcDateTime;
            challenge.ExpiresAt = challenge.CreatedAt.AddSeconds(settings.Value.ChallengeLifetimeSeconds);
            context.CustomerSocialChallenges.Add(challenge);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            if (!committed) context.ChangeTracker.Clear();
        }
        return new NativeSocialChallengeDto(1, challenge.Id, proof, challenge.Nonce, challenge.ExpiresAt);
    }
    private async Task<(bool Authenticated, string StampHash)> ReauthenticateAsync(Guid owner, string password, CancellationToken cancellationToken)
    {
        var user = await NativeSocialAccountGuard.AcquireAsync(context, owner, cancellationToken);
        if (string.IsNullOrEmpty(user.SecurityStamp) || user.DeletionScheduledAt is not null
            || !user.EmailConfirmed || users.SupportsUserLockout && await users.IsLockedOutAsync(user))
            throw ReauthenticationRequired();
        if (!await users.CheckPasswordAsync(user, password))
        {
            if (users.SupportsUserLockout && !(await users.AccessFailedAsync(user)).Succeeded)
                throw ReauthenticationRequired();
            return (false, string.Empty);
        }
        if (users.SupportsUserLockout && !(await users.ResetAccessFailedCountAsync(user)).Succeeded)
            throw ReauthenticationRequired();
        return (true, NativeSocialChallengeProof.Hash(user.SecurityStamp));
    }
    private static ConflictException ReauthenticationRequired() => new("The account changed. Sign in again.");
}
