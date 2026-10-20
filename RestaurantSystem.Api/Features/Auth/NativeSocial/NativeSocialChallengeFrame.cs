using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public sealed record NativeSocialChallengeFrame(Guid Id, Guid TenantId, string Provider, string Flow,
    string ProofHash, string Nonce, Guid? UserId, string? SecurityStampHash, DateTime CreatedAt, DateTime ExpiresAt)
{
    public static NativeSocialChallengeFrame From(CustomerSocialChallenge row) => new(row.Id, row.TenantId,
        row.Provider, row.Flow, row.ProofHash, row.Nonce, row.UserId, row.SecurityStampHash, row.CreatedAt, row.ExpiresAt);
}
