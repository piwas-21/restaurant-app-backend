using Google.Apis.Auth;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public interface INativeGoogleSignatureVerifier
{
    Task<GoogleJsonWebSignature.Payload?> ValidateAsync(string token, IReadOnlyList<string> audiences,
        CancellationToken cancellationToken);
}
