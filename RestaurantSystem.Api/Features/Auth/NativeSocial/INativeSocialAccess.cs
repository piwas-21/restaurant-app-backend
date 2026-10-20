namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeSocialAccess
{
    Task RequireEnabledAsync(string provider, CancellationToken cancellationToken);
}
