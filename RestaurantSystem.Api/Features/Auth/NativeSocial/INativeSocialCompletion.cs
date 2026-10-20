using RestaurantSystem.Api.Features.Auth.Dtos;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeSocialCompletion
{
    Task<AuthResponse?> CompleteAsync(Guid challengeId, string proof, string token,
        Guid? owner, NativeSocialProfile profile, CancellationToken cancellationToken);
}
