using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

/// <summary>Independent random proof and provider nonce; only a proof digest is stored.</summary>
public static class NativeSocialChallengeProof
{
    public static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsEncodedProof(string? value) => value is { Length: 43 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    public static bool Matches(string? raw, string expectedHash) => raw is { } value && IsEncodedProof(value)
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(expectedHash));
}
