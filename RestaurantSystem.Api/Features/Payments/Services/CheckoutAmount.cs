using System.Globalization;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>
/// A chargeable amount, resolved once from the PERSISTED order total and the tenant's currency.
///
/// <para>
/// Its reason for existing is that three separate things can make a total unchargeable — an
/// unusable currency label, a non-positive total, and Stripe's TWINT ceiling — and all three must
/// be answered BEFORE the Stripe call. Answered after, each one is a raw gateway error in front of
/// a diner instead of a sentence they can act on.
/// </para>
/// </summary>
public readonly record struct CheckoutAmount
{
    private const int MinorUnitsPerMajor = 100;

    /// <summary>
    /// Stripe enforces the TWINT ceiling at SESSION CREATION, not at payment — measured, plan §3:
    /// <c>code = amount_too_large</c>, max CHF 5,000.00. So the whole redirect fails, not just the
    /// TWINT tile, which is why this is a pre-check rather than something the diner discovers.
    /// </summary>
    private const long TwintMaxMinorChf = 5_000_00;

    private const string ChfCurrency = "CHF";

    /// <summary>
    /// Every currency Sofra can onboard a tenant in (plan §3: CH · FR · DE · NL · IT · ES · BE · AT ·
    /// GB · US · AE), and every one of them happens to be two-decimal — which is the ONLY reason
    /// <see cref="MinorUnitsPerMajor"/> can be a constant. An allow-list rather than a fallback
    /// because the failure mode of guessing is charging 100× or 1/100× the price: JPY is
    /// zero-decimal and BHD is three, so a tenant in an unlisted currency must be refused loudly
    /// here, not silently mis-scaled.
    /// </summary>
    private static readonly HashSet<string> SupportedCurrencies =
        new(StringComparer.OrdinalIgnoreCase) { ChfCurrency, "EUR", "GBP", "USD", "AED" };

    private CheckoutAmount(string currency, long minor)
    {
        Currency = currency;
        Minor = minor;
    }

    /// <summary>Lower-case ISO-4217, the casing Stripe returns and expects.</summary>
    public string Currency { get; }

    /// <summary>The amount in the currency's minor unit — what Stripe speaks.</summary>
    public long Minor { get; }

    /// <summary>
    /// Resolves a charge from an order total and the tenant's configured currency label.
    /// </summary>
    /// <param name="total">
    /// <c>order.Total</c> as persisted. Server-computed since S0b, and <c>decimal(10,2)</c> in the
    /// schema, so the ×100 below is exact rather than a rounding decision.
    /// </param>
    /// <param name="currencyLabel">
    /// <c>LocalizationSettings.Currency</c> — the tenant registry's <c>currency</c> field. Its own
    /// contract calls it "ISO-4217-ish" and it was display-only until now, so it is validated here
    /// rather than trusted: an operator typo reaching Stripe as a currency is a failed checkout.
    /// </param>
    public static CheckoutAmount From(decimal total, string? currencyLabel)
    {
        var currency = (currencyLabel ?? string.Empty).Trim();

        // The allow-list IS the whole check — a length or alphabet test in front of it could never
        // decide the outcome, since every member is already three ASCII letters.
        if (!SupportedCurrencies.Contains(currency))
        {
            // Names the value: this is an operator-facing misconfiguration, and a tenant whose
            // currency is simply not supported yet needs to be able to tell that apart from a bug.
            throw new BadRequestException(
                $"Online payment is not available in this restaurant's currency ('{currency}').");
        }

        if (total <= 0)
        {
            // Nothing to collect. Reaching Stripe with 0 would fail there anyway, and a "paid"
            // zero-amount session is exactly the shape S0b closed on the order side.
            throw new BadRequestException("This order has nothing left to pay.");
        }

        var minor = decimal.ToInt64(decimal.Round(total, 2, MidpointRounding.AwayFromZero) * MinorUnitsPerMajor);

        if (currency.Equals(ChfCurrency, StringComparison.OrdinalIgnoreCase) && minor > TwintMaxMinorChf)
        {
            throw new BadRequestException(string.Format(
                CultureInfo.InvariantCulture,
                "Online payment is not available for orders above {0:N2} {1}. Please pay at the restaurant.",
                TwintMaxMinorChf / (decimal)MinorUnitsPerMajor,
                ChfCurrency));
        }

        return new CheckoutAmount(currency.ToLowerInvariant(), minor);
    }
}
