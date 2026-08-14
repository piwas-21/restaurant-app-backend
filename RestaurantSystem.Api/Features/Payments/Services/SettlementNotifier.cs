using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class SettlementNotifier : ISettlementNotifier
{
    private readonly IOrderEventService _events;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderNotificationService _notifications;
    private readonly IAdminOrderAlertSender _adminAlerts;
    private readonly ILogger<SettlementNotifier> _logger;

    public SettlementNotifier(
        IOrderEventService events,
        IOrderMappingService mapping,
        IOrderNotificationService notifications,
        IAdminOrderAlertSender adminAlerts,
        ILogger<SettlementNotifier> logger)
    {
        _events = events;
        _mapping = mapping;
        _notifications = notifications;
        _adminAlerts = adminAlerts;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyConfirmedAsync(
        Order order, OrderStatus previousStatus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var dto = await _mapping.MapToOrderDtoAsync(order, cancellationToken);

        try
        {
            await _events.NotifyOrderStatusChanged(dto, previousStatus.ToString());
        }
        catch (Exception ex)
        {
            // The money is committed by the time this runs. A broadcast failure must not be able to
            // surface to the diner as a failed payment.
            _logger.LogError(
                ex, "Failed to broadcast confirmation for order {OrderNumber}", order.OrderNumber);
        }

        // The restaurant's alert. Order creation queues it for every other order, but an online one
        // is deliberately held Pending there, so until now a PAID order was the one kind the
        // operator never heard about by mail at all — the GAP-11 hole, on the orders that carry
        // money. Queued (never awaited) and ledger-claimed, so the settle path cannot be slowed or
        // failed by mail, and a reconciler replay cannot mail the restaurant twice.
        _adminAlerts.Queue(dto);

        // Already swallows its own failures — the same call order creation makes for a dine-in order
        // it confirms on the spot. This is that email, sent at the moment the money actually arrived.
        // The guest gets this rather than the "we have your order" receipt: their order is not
        // pending anything, it is paid and confirmed.
        await _notifications.SendOrderConfirmedAsync(
            order, OrderNotificationService.DefaultDineInPreparationMinutes, cancellationToken);
    }
}
