using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeSocialAccounts
{
    Task<ApplicationUser> ResolveAsync(NativeSocialIdentity identity, NativeSocialProfile profile,
        CustomerSocialChallenge challenge, CancellationToken cancellationToken);
}
