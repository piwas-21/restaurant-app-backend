namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeSocialChallenges
{
    Task<NativeSocialChallengeDto> CreateAsync(string provider, Guid? owner, string? currentPassword, CancellationToken cancellationToken);
}
