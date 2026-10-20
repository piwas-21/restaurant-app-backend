namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeSocialAccess
{
    Task<Guid> RequireEnabledAsync(string provider, CancellationToken cancellationToken);
}
