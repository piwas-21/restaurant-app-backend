using System.Security.Cryptography;
using System.Globalization;
using System.Text;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public static class TableGuestCredentialCrypto
{
    private const int ParticipantSecretLength = 32;

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

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
