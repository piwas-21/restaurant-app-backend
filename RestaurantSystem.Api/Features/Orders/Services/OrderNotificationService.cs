using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderNotificationService : IOrderNotificationService
{
    private const int DineInDefaultPrepMinutes = 15;

    private readonly IEmailService _emailService;
    private readonly IEmailLanguageResolver _languages;
    private readonly IOrderEventService _orderEventService;
    private readonly IGuestOrderReceiptSender _receipts;
    private readonly IAdminOrderAlertSender _adminAlerts;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IEmailService emailService,
        IEmailLanguageResolver languages,
        IOrderEventService orderEventService,
        IGuestOrderReceiptSender receipts,
        IAdminOrderAlertSender adminAlerts,
        IOptions<EmailSettings> emailSettings,
        ILogger<OrderNotificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(emailSettings);

        _emailService = emailService;
        _languages = languages;
        _emailSettings = emailSettings.Value;
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
            // The order's frozen language. Both callers are staff or machine requests — a status
            // change made in the restaurant, and the Stripe settlement webhook, whose
            // Accept-Language is Stripe's (§6.1) — so the row is the only guest voice here.
            await _emailService.SendOrderConfirmedEmailAsync(
                _languages.ForGuest(order.PreferredLanguage),
                order.CustomerEmail,
                order.CustomerName ?? string.Empty,
                order.OrderNumber,
                order.Type.ToString(),
                estimatedPreparationMinutes);

            // The order number, not the address: this line now also covers the staff status-change
            // path, and the recipient's email is PII this log has no need to carry
            // (docs/privacy/pii-inventory.md).
            _logger.LogInformation("Sent order-confirmed email for order {OrderNumber}", order.OrderNumber);
        }
        catch (Exception ex)
        {
            // Order creation must not fail because email did — preserved
            // verbatim from the inline handler block.
            _logger.LogError(ex, "Failed to send order-confirmed email for order {OrderNumber}", order.OrderNumber);
        }
    }

    public async Task SendOrderDelayedAsync(Order order, int delayMinutes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrEmpty(order.CustomerEmail))
        {
            return;
        }

        try
        {
            // The order's own language (§1 rank 1). A delay is announced by staff, so the request
            // this runs on is the restaurant's, not the guest's (§6.10).
            var baseUrl = _emailSettings.BackendBaseUrl;

            await _emailService.SendOrderDelayedEmailAsync(
                _languages.ForGuest(order.PreferredLanguage),
                order.CustomerEmail,
                order.CustomerName ?? string.Empty,
                order.OrderNumber,
                delayMinutes,
                $"{baseUrl}/api/orders/{order.Id}/approve-delay",
                $"{baseUrl}/api/orders/{order.Id}/reject-delay");

            _logger.LogInformation("Sent order-delayed email for order {OrderNumber}", order.OrderNumber);
        }
        catch (Exception ex)
        {
            // A status change that is already saved must not fail over a mail.
            _logger.LogError(ex, "Failed to send order delayed email for order {OrderNumber}", order.OrderNumber);
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
