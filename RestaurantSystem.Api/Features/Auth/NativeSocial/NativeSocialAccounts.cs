using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Provider subject/issuer binding under the transaction's provider/account locks.</summary>
public sealed class NativeSocialAccounts(ApplicationDbContext context, UserManager<ApplicationUser> users,
    TimeProvider time) : INativeSocialAccounts
{
    public async Task<ApplicationUser> ResolveAsync(NativeSocialIdentity identity, NativeSocialProfile profile,
        CustomerSocialChallenge challenge, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null || context.ChangeTracker.HasChanges()
            || identity.Provider != challenge.Provider)
            throw new ConflictException("The account changed. Start sign-in again.");
        var binding = await context.CustomerSocialIdentities.AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == challenge.TenantId && row.Provider == identity.Provider
            && row.Issuer == identity.Issuer && row.Subject == identity.Subject, cancellationToken);
        if (challenge.UserId is { } owner)
            return await LinkAsync(identity, challenge, binding, owner, cancellationToken);
        if (binding is not null)
        {
            var user = await NativeSocialAccountGuard.AcquireAsync(context, binding.UserId, cancellationToken);
            if (!user.EmailConfirmed || user.DeletionScheduledAt is not null) throw Invalid();
            return user;
        }
        if (!identity.EmailVerified || identity.Email is null) throw Invalid();
        // A provider-verified email is contact only. Even an exact match requires local reauth/link.
        if (await users.FindByEmailAsync(identity.Email) is not null)
            throw new BadRequestException("Sign in to the restaurant account and explicitly link this provider.", "NATIVE_SOCIAL_LINK_REQUIRED");
        var created = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = identity.Email,
            Email = identity.Email,
            EmailConfirmed = true,
            FirstName = ProfileName(profile.FirstName),
            LastName = ProfileName(profile.LastName),
            Role = UserRole.Customer,
            CreatedBy = "NativeGoogleAuth",
            CreatedAt = time.GetUtcNow().UtcDateTime,
            RefreshToken = string.Empty
        };
        if (!(await users.CreateAsync(created)).Succeeded) throw Invalid();
        context.CustomerSocialIdentities.Add(NewBinding(identity, challenge, created.Id));
        await context.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task<ApplicationUser> LinkAsync(NativeSocialIdentity identity, CustomerSocialChallenge challenge,
        CustomerSocialIdentity? binding, Guid owner, CancellationToken cancellationToken)
    {
        var user = await NativeSocialAccountGuard.AcquireAsync(context, owner, cancellationToken);
        if (!user.EmailConfirmed || user.DeletionScheduledAt is not null || user.SecurityStamp is null
            || NativeSocialChallengeProof.Hash(user.SecurityStamp) != challenge.SecurityStampHash
            || binding is not null && binding.UserId != owner)
            throw Invalid();
        if (binding is not null) return user;
        if (await context.CustomerSocialIdentities.AnyAsync(row => row.TenantId == challenge.TenantId
            && row.UserId == owner && row.Provider == identity.Provider, cancellationToken))
            throw new ConflictException("This restaurant account already has a different provider identity.");
        context.CustomerSocialIdentities.Add(NewBinding(identity, challenge, owner));
        await context.SaveChangesAsync(cancellationToken);
        return user;
    }

    private CustomerSocialIdentity NewBinding(NativeSocialIdentity identity, CustomerSocialChallenge challenge, Guid owner) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = challenge.TenantId,
        UserId = owner,
        Provider = identity.Provider,
        Issuer = identity.Issuer,
        Subject = identity.Subject,
        CreatedAt = time.GetUtcNow().UtcDateTime
    };
    private static string ProfileName(string? value)
    {
        if (value is null) throw new BadRequestException("Enter your name to create a restaurant account.", "NATIVE_SOCIAL_PROFILE_REQUIRED");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 50 || value.Any(char.IsControl)) throw Invalid();
        return value.Trim();
    }
    private static BadRequestException Invalid() => new("Provider sign-in could not be verified.");
}
