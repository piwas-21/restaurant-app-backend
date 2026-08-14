using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// M7, the guest's "we have your order" receipt. Sent at most once per order, guarded by the
/// <c>IOutboundEmailLedger</c>.
/// </summary>
public interface IGuestOrderReceiptSender
{
    /// <summary>
    /// Sends it on the caller's thread and lets a provider failure through, so the legacy resend
    /// endpoint can still report one. A failed send gives its claim back first.
    /// </summary>
    Task SendAsync(OrderDto order);

    /// <summary>
    /// Queues the same send on a detached task against a fresh DI scope, and returns immediately.
    /// <para>
    /// This is what order creation uses. <c>IEmailService</c> retries a failed send three times a
    /// second apart with no timeout budget, so awaiting it inside <c>POST /api/orders</c> would put
    /// a degraded mail provider in front of the request that places the order — the guest would see
    /// "order failed" for an order that is committed, and place it again. The mail must never be
    /// able to do that; the ledger, not the await, is what stops a duplicate.
    /// </para>
    /// </summary>
    void Queue(OrderDto order);
}
