using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Consume a verified nonce once, then execute only that live original under fresh guards.</summary>
public sealed class NativeSocialCompletion(ApplicationDbContext context, TimeProvider time,
    IOptions<CustomerMobileSettings> mobile, INativeSocialAccess access, INativeGoogleIdentityVerifier verifier,
    INativeSocialAccounts accounts, ITokenService tokens, IRefreshSessionService sessions) : INativeSocialCompletion
{
    public async Task<AuthResponse?> CompleteAsync(Guid challengeId, string proof, string token,
        Guid? owner, NativeSocialProfile profile, CancellationToken cancellationToken)
    {
        await access.RequireEnabledAsync("google", cancellationToken);
        RequireCleanContext();
        var original = await context.CustomerSocialChallenges.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == challengeId && row.TenantId == mobile.Value.TenantId, cancellationToken);
        original = RequireOriginal(original, proof, owner, false);
        // No transaction/lock spans provider key fetching or token signature validation.
        var identity = await verifier.VerifyAsync(token, original.Nonce, cancellationToken);
        var frame = NativeSocialChallengeFrame.From(original);
        var consumedAt = await ConsumeAsync(frame, identity, proof, owner, cancellationToken);
        return await ExecuteAsync(frame, consumedAt, identity, proof, owner, profile, cancellationToken);
    }

    private async Task<DateTime> ConsumeAsync(NativeSocialChallengeFrame frame, NativeSocialIdentity identity,
        string proof, Guid? owner, CancellationToken cancellationToken)
    {
        RequireCleanContext();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await NativeSocialLocks.IdentityAsync(context, frame.TenantId, identity, cancellationToken);
            var row = RequireOriginal(await LockedAsync(frame.Id, cancellationToken), proof, owner, false);
            if (NativeSocialChallengeFrame.From(row) != frame) throw Invalid();
            await access.RequireEnabledAsync("google", cancellationToken);
            var now = time.GetUtcNow().UtcDateTime;
            await context.CustomerSocialChallenges.Where(value => value.Id == frame.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.ConsumedAt, now), cancellationToken);
            var consumed = await context.CustomerSocialChallenges.AsNoTracking().SingleAsync(value => value.Id == frame.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return consumed.ConsumedAt ?? throw Invalid();
        }
        finally
        {
            if (!committed) context.ChangeTracker.Clear();
        }
    }

    private async Task<AuthResponse?> ExecuteAsync(NativeSocialChallengeFrame frame, DateTime consumedAt,
        NativeSocialIdentity identity, string proof, Guid? owner, NativeSocialProfile profile, CancellationToken cancellationToken)
    {
        RequireCleanContext();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            await NativeSocialLocks.IdentityAsync(context, frame.TenantId, identity, cancellationToken);
            // Erasure locks User before cascading into owned challenges. Preserve that order.
            if (owner is { } userId)
                await NativeSocialAccountGuard.AcquireAsync(context, userId, cancellationToken);
            var row = RequireOriginal(await LockedAsync(frame.Id, cancellationToken), proof, owner, true);
            if (NativeSocialChallengeFrame.From(row) != frame || row.ConsumedAt != consumedAt) throw Invalid();
            await access.RequireEnabledAsync("google", cancellationToken);
            var user = await accounts.ResolveAsync(identity, profile, row, cancellationToken);
            RequireLive(frame);
            AuthResponse? response = null;
            if (owner is null)
            {
                var refresh = await sessions.IssueAsync(user, cancellationToken);
                RequireLive(frame);
                response = new AuthResponse
                {
                    AccessToken = tokens.GenerateAccessToken(user),
                    RefreshToken = refresh,
                    UserId = user.Id,
                    Email = user.Email ?? string.Empty,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Role = user.Role,
                    Expiration = tokens.GetAccessTokenExpiration()
                };
            }
            RequireLive(frame);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return response;
        }
        finally
        {
            if (!committed) context.ChangeTracker.Clear();
        }
    }

    private Task<CustomerSocialChallenge?> LockedAsync(Guid id, CancellationToken cancellationToken) =>
        context.CustomerSocialChallenges.FromSqlInterpolated($"SELECT * FROM customer_social_challenges WHERE id = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    private CustomerSocialChallenge RequireOriginal(CustomerSocialChallenge? row, string proof, Guid? owner, bool consumed)
    {
        if (row is null || row.TenantId != mobile.Value.TenantId || row.Provider != "google"
            || row.UserId != owner || row.Flow != (owner is null ? "login" : "link")
            || (row.ConsumedAt is not null) != consumed || !NativeSocialChallengeProof.Matches(proof, row.ProofHash))
            throw Invalid();
        RequireLive(NativeSocialChallengeFrame.From(row));
        return row;
    }
    private void RequireLive(NativeSocialChallengeFrame frame)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (frame.CreatedAt > now || frame.ExpiresAt <= now || frame.ExpiresAt <= frame.CreatedAt
            || frame.ExpiresAt - frame.CreatedAt > TimeSpan.FromMinutes(5)) throw Invalid();
    }
    private void RequireCleanContext()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new ConflictException("The account changed. Start sign-in again.");
    }
    private static BadRequestException Invalid() => new("Provider sign-in could not be verified.");
}
