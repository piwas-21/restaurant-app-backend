using Google.Apis.Auth;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Google's validator owns trusted keys, signed algorithm, issuer, audience and lifetime.</summary>
public sealed class NativeGoogleSignatureVerifier : INativeGoogleSignatureVerifier
{
    public async Task<GoogleJsonWebSignature.Payload?> ValidateAsync(string token,
        IReadOnlyList<string> audiences, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await GoogleJsonWebSignature.ValidateAsync(token,
                new GoogleJsonWebSignature.ValidationSettings { Audience = audiences })
                .WaitAsync(cancellationToken);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
