using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>
/// Sofra's own cut of a tenant→diner charge, resolved once from the ALREADY-VALIDATED
/// <see cref="CheckoutAmount"/> and the tenant's configured commission rate.
///
/// <para>
/// Its reason for existing, rather than inlining the multiply where the checkout-session request is
/// built, is a MEASURED Stripe behaviour our own code has to guard because Stripe will not: an
/// <c>application_fee_amount</c> that EXCEEDS the charge is not rejected — Stripe silently CAPS it
/// at 100% of the charge. Measured 2026-09-04: a confirmed PaymentIntent for 4000 CHF-minor with a
/// requested fee of 5000 produced an ApplicationFee of <c>amount=4000</c>, leaving the restaurant
/// with nothing and no error anywhere. Every rule below exists to keep a configuration mistake from
/// ever reaching Stripe as a fee Stripe would go on to silently cap.
/// </para>
/// </summary>
/// <remarks>
/// A static class, deliberately, where its sibling <see cref="CheckoutAmount"/> is a
/// <c>readonly record struct</c>. The difference is that a <c>CheckoutAmount</c> is a VALUE the rest
/// of the flow carries around (a currency and a minor amount travel together), whereas a commission
/// resolves to a single nullable <c>long</c> that goes straight onto the request. A record struct
/// here would be an instance nobody ever constructs — equality, a parameterless constructor and all.
/// </remarks>
public static class CheckoutCommission
{
    private const int BpsDenominator = 10_000;

    /// <summary>
    /// 10%. Stripe does not enforce any ceiling of its own — see the class doc — so this is the
    /// only thing standing between a configuration typo and Stripe confiscating a whole order.
    /// </summary>
    private const int MaxBps = 1000;

    /// <summary>
    /// Resolves Sofra's fee in minor units for one charge, or <c>null</c> when no
    /// <c>application_fee_amount</c> parameter should be sent at all.
    /// </summary>
    /// <param name="amount">
    /// The already-validated chargeable amount (<see cref="CheckoutAmount.From"/> has already run).
    /// The fee is a percentage OF this, never of the raw order total, so it inherits every guarantee
    /// that call already established (currency support, the TWINT ceiling, a positive total).
    /// </param>
    /// <param name="bps">
    /// <c>StripeCommissionSettings.Bps</c>, basis points — e.g. 150 = 1.5%.
    /// </param>
    public static long? From(CheckoutAmount amount, int bps)
    {
        // Zero is the inert default (Settings/StripeCommissionSettings.cs). Returning null rather
        // than 0 here — and StripeCheckoutClient only setting PaymentIntentData when this is
        // non-null — is what keeps every existing tenant's Stripe request BYTE-IDENTICAL to before
        // this feature existed.
        if (bps == 0) return null;

        if (bps < 0)
        {
            // A negative rate cannot come from an operator typing a percentage; it is a
            // configuration error and must be caught here, not as a confusing Stripe-side failure
            // at checkout time.
            throw new BadRequestException($"Commission rate cannot be negative ({bps} bps).");
        }

        if (bps > MaxBps)
        {
            // Stripe would not refuse this for us — it would silently cap the fee at the charge
            // amount instead (see the class doc), so this is the only place a misconfigured rate
            // can be caught before it reaches Stripe.
            throw new BadRequestException(
                $"Commission rate ({bps} bps) exceeds the {MaxBps} bps (10%) ceiling.");
        }

        var fee = decimal.ToInt64(decimal.Round(
            amount.Minor * (decimal)bps / BpsDenominator, 0, MidpointRounding.AwayFromZero));

        // Stripe requires a POSITIVE application_fee_amount. A fee that rounds down to 0 is not the
        // same request as sending none, so it must fall back to "send nothing" rather than reaching
        // Stripe as an explicit, rejected 0.
        if (fee == 0) return null;

        // Unreachable arithmetic with the 10% ceiling above — but this is the guard that stands
        // between a FUTURE ceiling change and Stripe confiscating a whole order. Stripe caps rather
        // than rejects an over-large fee (see the class doc), so without this, raising MaxBps past
        // 100% (or a bug that lets bps arrive uncapped) would silently hand a diner's entire payment
        // to Sofra's platform balance instead of the restaurant's, with no error from Stripe to
        // catch it.
        if (fee >= amount.Minor)
        {
            throw new BadRequestException(
                $"Computed commission ({fee}) would leave the restaurant nothing from a charge of {amount.Minor}.");
        }

        return fee;
    }
}
