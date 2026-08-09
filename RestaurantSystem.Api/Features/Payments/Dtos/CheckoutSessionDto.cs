namespace RestaurantSystem.Api.Features.Payments.Dtos;

/// <summary>
/// What the diner's browser needs in order to be redirected to Stripe's hosted Checkout.
///
/// <para>
/// Deliberately narrow. The endpoint that returns this is ANONYMOUS (a guest checkout has no
/// account — ADR-004), so anything on it is readable by anyone holding an order id. Amount and
/// currency are here because Stripe's own page shows them anyway; the customer's name, items and
/// address are not. Note the scope of that claim: <see cref="Url"/> leads to a Stripe page, so
/// what it displays is exposed too — which is why <c>customer_email</c> is not prefilled there.
/// </para>
/// </summary>
public record CheckoutSessionDto
{
    /// <summary>Stripe <c>cs_...</c>. The client hands it back on the return trip (S9).</summary>
    public required string SessionId { get; init; }

    /// <summary>The hosted Checkout page to redirect to.</summary>
    public required string Url { get; init; }

    /// <summary>+30 minutes from minting — the documented Stripe minimum, not the 24 h default.</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Lower-case ISO-4217, as Stripe returns it.</summary>
    public required string Currency { get; init; }

    /// <summary>Minor units, matching what was recorded and what Stripe will charge.</summary>
    public long AmountMinor { get; init; }

    /// <summary>
    /// Built from the PERSISTED session row's currency and amount, never from Stripe's echo of
    /// them: that row is what S5 asserts against, so describing the charge any other way would be
    /// describing a different charge.
    /// </summary>
    public static CheckoutSessionDto From(
        string sessionId, string url, DateTime expiresAt, string currency, long amountMinor) =>
        new()
        {
            SessionId = sessionId,
            Url = url,
            ExpiresAt = expiresAt,
            Currency = currency,
            AmountMinor = amountMinor,
        };
}
