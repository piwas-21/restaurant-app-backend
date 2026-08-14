using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Api.Features.Orders.Dtos;
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
    private readonly IGuestOrderReceiptSender _receipts;
    private readonly IAdminOrderAlertSender _adminAlerts;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IEmailService emailService,
        IOrderEventService orderEventService,
        IGuestOrderReceiptSender receipts,
        IAdminOrderAlertSender adminAlerts,
        ILogger<OrderNotificationService> logger)
    {
        _emailService = emailService;
        _orderEventService = orderEventService;
        _receipts = receipts;
        _adminAlerts = adminAlerts;
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
            await _emailService.SendOrderConfirmedEmailAsync(EmailCultures.English,
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

        // Queued first, and separately from the guest's: the restaurant's alert must not share a
        // failure fate with a mail to an address the guest may well have typed wrong. One diner's
        // typo silencing the operator is the exact failure GAP-11 exists to remove.
        _adminAlerts.Queue(orderDto);
        _receipts.Queue(orderDto);

        // Dine-in auto-confirms, so it gets the confirmed mail too — exactly what
        // CreateOrderCommandHandler did inline before GAP-11 moved the decision in here. Still
        // awaited, as it has always been; the two queued mails above are the ones that used to be
        // the browser's problem and must not become the request's.
        if (order.Type == OrderType.DineIn)
        {
            await SendOrderConfirmedAsync(order, DefaultDineInPreparationMinutes, cancellationToken);
        }
    }

    public async Task SendOrderConfirmationAsync(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);

        try
        {
            // Awaited within the caller's request scope, so the resend endpoint still reports a
            // provider failure to whoever asked for the resend.
            await _receipts.SendAsync(order);
        }
        finally
        {
            // finally, not after: see SendNewOrderMailAsync. The operator's alert is queued even
            // when the guest's address is the thing that failed.
            _adminAlerts.Queue(order);
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
