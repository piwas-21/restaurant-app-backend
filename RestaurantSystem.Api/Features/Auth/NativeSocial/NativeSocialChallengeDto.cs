namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public sealed record NativeSocialChallengeDto(int SchemaVersion, Guid ChallengeId, string Proof,
    string Nonce, DateTime ExpiresAt);
