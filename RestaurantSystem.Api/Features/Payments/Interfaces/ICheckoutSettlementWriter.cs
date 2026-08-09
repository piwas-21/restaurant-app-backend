using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Applies everything that follows from Stripe having taken the money: it claims the session row,
/// completes the tender, recomputes the order's balance, performs the confirm that order creation
/// deferred, awards fidelity points and notifies.
///
/// <para>
/// Split from the settle command because the two do different jobs and fail differently. The
/// command DECIDES — it asks Stripe and refuses to proceed on anything it does not like. This
/// COMMITS, and once it starts there is money on the other side of the decision, so every step
/// runs inside one transaction that either lands whole or not at all.
/// </para>
/// </summary>
public interface ICheckoutSettlementWriter
{
    /// <summary>
    /// Settles <paramref name="session"/>, exactly once across all callers.
    /// </summary>
    /// <remarks>
    /// Safe to call concurrently and safe to call again: the claim is a conditional UPDATE on the
    /// session's status, so of two callers arriving together exactly one does the work and the
    /// other reports what it finds. Both get an accurate answer.
    /// </remarks>
    /// <param name="session">The local row, already matched against Stripe by the caller.</param>
    /// <param name="paymentIntentId">Stripe's <c>pi_...</c>, recorded on both the row and the tender.</param>
    /// <param name="amountReceivedMinor">
    /// Stripe's <c>amount_total</c>. The caller has already asserted it equals the row's
    /// <c>AmountMinor</c>; it is passed rather than re-derived so the tender records the number
    /// STRIPE reported, not the one we hoped for.
    /// </param>
    Task<CheckoutSettlementDto> SettleAsync(
        OrderCheckoutSession session,
        string? paymentIntentId,
        long? amountReceivedMinor,
        CancellationToken cancellationToken);
}
