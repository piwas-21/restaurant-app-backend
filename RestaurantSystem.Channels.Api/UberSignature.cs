using System.Security.Cryptography;
using System.Text;

namespace RestaurantSystem.Channels.Api;

public static class UberSignature
{
    public static bool Verify(ReadOnlySpan<byte> body, string signature, string secret)
    {
        // Reject duplicate/comma-separated headers, prefixes and malformed values before decoding.
        if (signature.Length != 64 || signature.Any(c => !char.IsAsciiHexDigit(c)))
            return false;
        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        var expected = Convert.FromHexString(signature);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
