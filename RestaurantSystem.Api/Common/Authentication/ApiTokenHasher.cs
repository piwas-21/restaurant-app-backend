using System.Security.Cryptography;
using System.Text;

namespace RestaurantSystem.Api.Common.Authentication;

/// <summary>
/// Mints and hashes machine tokens (docs/plans/API-TOKENS-PLAN.md §4).
/// </summary>
/// <remarks>
/// Plain SHA-256, deliberately NOT bcrypt/argon2: the input is 256 bits of CSPRNG output, not a
/// human password, so there is no dictionary for a slow KDF to defend against — and its cost
/// would be paid on EVERY authenticated request. It also keeps the lookup a single indexed
/// equality match instead of a scan with a per-row verify.
/// </remarks>
public static class ApiTokenHasher
{
    /// <summary>Number of random bytes behind a token. 256 bits — unguessable, and short enough to paste.</summary>
    private const int TokenBytes = 32;

    /// <summary>Characters of the plaintext kept for display in the admin list.</summary>
    public const int PrefixLength = 12;

    /// <summary>Creates a new plaintext token. The ONLY place a plaintext exists.</summary>
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        // Base64Url: no '+', '/' or '=' to be mangled by a shell, a URL or a copy-paste.
        return ApiTokenDefaults.TokenPrefix + Base64UrlEncode(bytes);
    }

    /// <summary>Base64 SHA-256 of the plaintext — what is stored and what authentication looks up.</summary>
    public static string ComputeHash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
    }

    /// <summary>The display prefix of a plaintext, e.g. <c>sk_live_a1b2</c>.</summary>
    public static string ExtractPrefix(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return plaintext.Length <= PrefixLength ? plaintext : plaintext[..PrefixLength];
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
