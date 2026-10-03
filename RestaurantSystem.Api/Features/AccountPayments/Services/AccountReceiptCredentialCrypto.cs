using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>A receipt grant is a separate capability from access to an open table visit.</summary>
public static class AccountReceiptCredentialCrypto
{
    public const string HeaderName = "X-Account-Payment-Receipt";
    private const string HashDomain = "table-account-payment-receipt-v1\n";

    public static bool TryHash(string? token, out string receiptHash)
    {
        receiptHash = string.Empty;
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(token, out var secretDigest))
            return false;
        receiptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HashDomain + secretDigest)));
        return true;
    }

    public static bool Verify(string? token, string? expectedHash)
    {
        if (!TryHash(token, out var actualHash) || expectedHash?.Length != actualHash.Length)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
