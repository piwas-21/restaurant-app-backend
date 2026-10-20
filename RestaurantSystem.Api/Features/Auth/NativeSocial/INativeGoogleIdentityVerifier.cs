namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeGoogleIdentityVerifier
{
    Task<NativeSocialIdentity> VerifyAsync(string token, string expectedNonce,
        CancellationToken cancellationToken);
}
