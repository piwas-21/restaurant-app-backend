using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Ends a checkout session that will never be paid, and releases the order it was holding.
///
/// <para>
/// Its own seam rather than a private method on the settle handler because retiring is half of a
/// pair: settling clears the <c>Processing</c> tender by COMPLETING it, and this is the only other
/// thing that clears it at all. A session that ends without either leaves a tender nothing can
/// resolve, and <c>UpdateOrderStatusCommand</c> reads that tender when it decides whether an order
/// may be confirmed.
/// </para>
/// </summary>
public interface ICheckoutSessionRetirement
{
    /// <summary>
    /// Moves the session to a terminal status, and fails the online tender it was covering.
    /// </summary>
    /// <remarks>
    /// The session update is conditional on the row still being <c>Created</c>, so a settle running
    /// concurrently wins: without that, a reconciler sweep that read "expired" a moment before the
    /// return trip settled could overwrite a <c>Completed</c> row and lose the only local record of
    /// money Stripe has already taken.
    /// </remarks>
    /// <param name="status">
    /// <c>Expired</c> or <c>Failed</c>. Both are terminal; the distinction is for support and for
    /// the reconciler, not for the diner.
    /// </param>
    Task RetireAsync(
        OrderCheckoutSession session,
        CheckoutSessionStatus status,
        string reason,
        CancellationToken cancellationToken);
}
