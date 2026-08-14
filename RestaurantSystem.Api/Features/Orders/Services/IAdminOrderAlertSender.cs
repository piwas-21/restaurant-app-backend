using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// M14, the restaurant's new-order alert — the only email notice a restaurant gets that an order
/// exists. Sent at most once per order, guarded by the <c>IOutboundEmailLedger</c>: since GAP-11
/// the server queues it at order creation, while the guest's browser can still ask for the same
/// mail through the legacy confirmation endpoint.
/// </summary>
public interface IAdminOrderAlertSender
{
    /// <summary>
    /// Queues the alert on a detached task against a fresh DI scope and returns immediately. Never
    /// throws: the operator's alert must not be able to fail a guest's order.
    /// </summary>
    void Queue(OrderDto order);
}
