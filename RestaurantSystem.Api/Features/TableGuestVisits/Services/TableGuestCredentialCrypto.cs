using System.Security.Cryptography;
using System.Globalization;
using System.Text;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public static class TableGuestCredentialCrypto
{
    public const int AdmissionCodeLength = 10;
    public const int AdmissionHashIterations = 210_000;
    private const int SaltLength = 16;
    private const int DigestLength = 32;
    private const int ParticipantSecretLength = 32;
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static (string Code, string Hash) CreateAdmissionCode()
    {
        var random = RandomNumberGenerator.GetBytes(AdmissionCodeLength);
        var code = new string(random.Select(value => CrockfordAlphabet[value & 31]).ToArray());
        return (code, HashAdmissionCode(code));
    }

    public static string HashAdmissionCode(string code)
    {
        if (!TryNormalizeAdmissionCode(code, out var normalized))
        {
            throw new ArgumentException("Admission code is invalid.", nameof(code));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var digest = Rfc2898DeriveBytes.Pbkdf2(
            normalized, salt, AdmissionHashIterations, HashAlgorithmName.SHA256, DigestLength);
        return $"pbkdf2-sha256${AdmissionHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(digest)}";
    }

    public static bool VerifyAdmissionCode(string code, string encodedHash)
    {
        if (!TryNormalizeAdmissionCode(code, out var normalized)
            || !TryReadHash(encodedHash, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            normalized, salt, AdmissionHashIterations, HashAlgorithmName.SHA256, DigestLength);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string CreateParticipantToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(ParticipantSecretLength));

    public static bool TryHashParticipantToken(string? token, out string tokenHash)
    {
        tokenHash = string.Empty;
        if (string.IsNullOrEmpty(token) || token.Length != 43
            || token.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != ParticipantSecretLength || Base64UrlEncode(bytes) != token)
            {
                return false;
            }

            tokenHash = Convert.ToHexString(SHA256.HashData(bytes));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string HashBasketSession(string sessionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));

    public static string HashRoundRequest(
        Guid sessionId, Guid participantId, Guid operationId, long revision,
        string basketSessionHash, string expectedBasketFingerprint)
    {
        var payload = string.Create(CultureInfo.InvariantCulture,
            $"table-guest-round-v2\n{sessionId:N}\n{participantId:N}\n{operationId:N}\n{revision}\n{basketSessionHash}\n{expectedBasketFingerprint.ToUpperInvariant()}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static bool TryNormalizeAdmissionCode(string? code, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(code) || code.Length != AdmissionCodeLength)
        {
            return false;
        }

        var candidate = code.ToUpperInvariant();
        if (candidate.Any(character => CrockfordAlphabet.IndexOf(character) < 0))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool TryReadHash(string encodedHash, out byte[] salt, out byte[] digest)
    {
        salt = [];
        digest = [];
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256"
            || parts[1] != AdmissionHashIterations.ToString(CultureInfo.InvariantCulture))
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            digest = Convert.FromBase64String(parts[3]);
            return salt.Length == SaltLength && digest.Length == DigestLength;
        }
        catch (FormatException)
        {
            salt = [];
            digest = [];
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
