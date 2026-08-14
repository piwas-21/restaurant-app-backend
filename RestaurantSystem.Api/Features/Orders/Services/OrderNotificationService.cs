using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderNotificationService : IOrderNotificationService
{
    private const string FallbackCustomerName = "Valued Customer";
    private const int DineInDefaultPrepMinutes = 15;

    private readonly IEmailService _emailService;
    private readonly IOrderEventService _orderEventService;
    private readonly IAdminOrderAlertSender _adminAlerts;
    private readonly IOutboundEmailLedger _ledger;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IEmailService emailService,
        IOrderEventService orderEventService,
        IAdminOrderAlertSender adminAlerts,
        IOutboundEmailLedger ledger,
        ILogger<OrderNotificationService> logger)
    {
        _emailService = emailService;
        _orderEventService = orderEventService;
        _adminAlerts = adminAlerts;
        _ledger = ledger;
        _logger = logger;
    }

    public async Task SendOrderConfirmedAsync(Order order, int estimatedPreparationMinutes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrEmpty(order.CustomerEmail))
        {
            return;
        }

        try
        {
            await _emailService.SendOrderConfirmedEmailAsync(
                order.CustomerEmail,
                order.CustomerName ?? FallbackCustomerName,
                order.OrderNumber,
                order.Type.ToString(),
                estimatedPreparationMinutes);

            _logger.LogInformation(
                "Sent order-confirmed email for order {OrderNumber} to {Email}",
                order.OrderNumber, order.CustomerEmail);
        }
        catch (Exception ex)
        {
            // Order creation must not fail because email did — preserved
            // verbatim from the inline handler block.
            _logger.LogError(ex, "Failed to send order-confirmed email for order {OrderNumber}", order.OrderNumber);
        }
    }

    public async Task SendNewOrderMailAsync(Order order, OrderDto orderDto, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(orderDto);

        // Dine-in auto-confirms, so it gets the confirmed mail too — exactly what
        // CreateOrderCommandHandler did inline before GAP-11 moved the decision in here.
        if (order.Type == OrderType.DineIn)
        {
            await SendOrderConfirmedAsync(order, DefaultDineInPreparationMinutes, cancellationToken);
        }

        try
        {
            await SendOrderConfirmationAsync(orderDto);
        }
        catch (Exception ex)
        {
            // The order is committed by the time this runs, so a mail failure must not turn a
            // placed order into a 5xx. The failed send released its own claim, so the browser's
            // legacy call — or a later resend — can still deliver it.
            _logger.LogError(
                ex, "Failed to send order-received email for order {OrderNumber}", orderDto.OrderNumber);
        }
    }

    public async Task SendOrderConfirmationAsync(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Customer email: awaited within the caller's request scope, so the legacy endpoint keeps
        // reporting its failure. Admin email: queued, never awaited.
        await SendOrderReceivedAsync(order);

        _adminAlerts.Queue(order);
    }

    private async Task SendOrderReceivedAsync(OrderDto order)
    {
        if (string.IsNullOrWhiteSpace(order.CustomerEmail))
        {
            // GAP-13: the old fallback really did post to noemail@example.com. Now that the server
            // sends this mail unprompted, that would be a guaranteed hard bounce on every emailless
            // order, charged to the tenant's own sending reputation.
            _logger.LogInformation(
                "Order {OrderNumber} has no customer email; skipping the order-received mail",
                order.OrderNumber);
            return;
        }

        if (!await _ledger.TryClaimAsync(OutboundEmailTypes.OrderReceived, order.Id))
        {
            _logger.LogInformation(
                "Order-received mail for order {OrderNumber} is already sent or in flight; skipping",
                order.OrderNumber);
            return;
        }

        try
        {
            await _emailService.SendOrderReceivedEmailAsync(
                order.CustomerEmail,
                order.CustomerName ?? FallbackCustomerName,
                order.OrderNumber,
                order.Type,
                order.Total,
                OrderEmailComposer.ComposeItems(order),
                order.Notes,
                OrderEmailComposer.ComposeDeliveryAddress(order));

            await _ledger.MarkSentAsync(OutboundEmailTypes.OrderReceived, order.Id);
        }
        catch
        {
            // Give the claim back so this order's receipt is still sendable; the caller decides
            // whether the failure is visible (the endpoint 400s, order creation logs it).
            await _ledger.ReleaseAsync(OutboundEmailTypes.OrderReceived, order.Id);
            throw;
        }
    }

    public async Task NotifyOrderCreatedAsync(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        try
        {
            _logger.LogInformation("Attempting to notify clients of order creation: {OrderNumber}", order.OrderNumber);
            await _orderEventService.NotifyOrderCreated(order);
            _logger.LogInformation("Successfully notified clients of order creation: {OrderNumber}", order.OrderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to notify clients of order creation for {OrderNumber}, but order was created successfully",
                order.OrderNumber);
        }
    }

    public async Task NotifyFocusOrderUpdateAsync(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (!order.IsFocusOrder)
        {
            return;
        }

        try
        {
            await _orderEventService.NotifyFocusOrderUpdate(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to notify clients of focus order update for {OrderNumber}", order.OrderNumber);
        }
    }

    // The DineInDefaultPrepMinutes const is exposed for the settle path, which sends this same
    // email at the moment the money arrives.
    public const int DefaultDineInPreparationMinutes = DineInDefaultPrepMinutes;
}
