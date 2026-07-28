using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Mints and checks the bearer secret carried by the anonymous quick-confirm / quick-cancel
/// links in the admin order email (<see cref="RestaurantSystem.Domain.Entities.Order.QuickActionToken"/>).
/// </summary>
/// <remarks>
/// Static rather than an injected service, unlike its <c>IOrderNumberGenerator</c> sibling: that
/// one reads the database to allocate a sequence, this is a pure function over the system CSPRNG
/// with nothing to configure and nothing worth substituting in a test.
/// </remarks>
public static class QuickActionTokens
{
    /// <summary>
    /// 256 bits. The token is the only thing standing between an anonymous caller and cancelling
    /// an order, and it never expires, so it is sized to be brute-force-proof rather than tidy.
    /// </summary>
    private const int EntropyBytes = 32;

    /// <summary>Base64url (RFC 4648 §5) — URL-safe, so it needs no escaping in the email link.</summary>
    public static string Generate() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyBytes));

    /// <summary>
    /// Compares a caller-supplied token against the stored one without leaking, through timing,
    /// how many leading characters were correct.
    /// </summary>
    /// <remarks>
    /// A plain <c>==</c> on strings short-circuits at the first differing character. Length is not
    /// hidden — <see cref="CryptographicOperations.FixedTimeEquals"/> returns immediately on a
    /// mismatch — which is harmless here because every issued token is the same length.
    /// <para>
    /// A null or empty <paramref name="stored"/> never matches: that is the state of every order
    /// created before the column existed, and it must not be reachable by sending an empty token.
    /// </para>
    /// </remarks>
    public static bool Matches(string? stored, string? supplied)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(supplied));
    }
}
