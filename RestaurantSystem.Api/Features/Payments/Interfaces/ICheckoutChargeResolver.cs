using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Decides, in one place, what a given order costs at Stripe: the chargeable amount AND Sofra's cut
/// of it.
///
/// <para>
/// A seam of its own because those two answers are read from the same kind of place (tenant
/// configuration) and are always needed together, so the alternative is every caller injecting both
/// <c>LocalizationSettings</c> and <c>StripeCommissionSettings</c> and remembering to consult them
/// in the right order. It also keeps the checkout handler an orchestrator: it asks what to charge,
/// it does not compute it.
/// </para>
/// </summary>
public interface ICheckoutChargeResolver
{
    /// <summary>
    /// Resolves the charge from an order total as PERSISTED. Throws
    /// <see cref="Common.Exceptions.BadRequestException"/> for anything unchargeable — an
    /// unsupported currency, a non-positive total, an over-TWINT-ceiling amount, or a
    /// misconfigured commission rate — so a caller never has to distinguish those.
    /// </summary>
    CheckoutCharge Resolve(decimal orderTotal);
}

/// <summary>
/// What Stripe is asked for: the amount, and the application fee that rides on it.
/// </summary>
/// <param name="Amount">The validated chargeable amount.</param>
/// <param name="ApplicationFeeMinor">
/// Sofra's fee in minor units, or <c>null</c> for "send no <c>application_fee_amount</c> at all" —
/// which is every tenant on the inert default, and is deliberately distinct from a fee of zero.
/// </param>
public readonly record struct CheckoutCharge(CheckoutAmount Amount, long? ApplicationFeeMinor);
