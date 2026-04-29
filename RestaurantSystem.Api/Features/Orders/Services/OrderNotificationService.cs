using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderNotificationService : IOrderNotificationService
{
    private const string FallbackCustomerName = "Valued Customer";
    private const string FallbackCustomerEmail = "noemail@example.com";
    private const string FallbackPhone = "Not provided";
    private const int DineInDefaultPrepMinutes = 15;

    private readonly IEmailService _emailService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IEmailService emailService,
        IServiceScopeFactory scopeFactory,
        IOptions<EmailSettings> emailSettings,
        ILogger<OrderNotificationService> logger)
    {
        _emailService = emailService;
        _scopeFactory = scopeFactory;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task SendOrderConfirmedAsync(Order order, int estimatedPreparationMinutes, CancellationToken cancellationToken)
    {
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

    public async Task SendOrderConfirmationAsync(OrderDto order)
    {
        var items = ComposeEmailItems(order);
        var deliveryAddress = ComposeDeliveryAddress(order);

        // Customer email: awaited within the caller's request scope.
        await _emailService.SendOrderReceivedEmailAsync(
            order.CustomerEmail ?? FallbackCustomerEmail,
            order.CustomerName ?? FallbackCustomerName,
            order.OrderNumber,
            order.Type.ToString(),
            order.Total,
            items,
            order.Notes,
            deliveryAddress);

        // Admin email: fire-and-forget. The original (pre-task-2.10) code
        // captured the request-scoped IEmailService inside Task.Run, which
        // led to ObjectDisposedException when SMTP I/O outlasted the
        // response (issue #13). Fix: capture the IServiceScopeFactory
        // (Singleton lifetime) and resolve a fresh IEmailService inside
        // the lambda's scope.
        var scopeFactory = _scopeFactory;
        var adminEmail = _emailSettings.AdminEmail;
        var logger = _logger;
        var orderNumber = order.OrderNumber;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await emailService.SendOrderConfirmationAdminEmailAsync(
                    adminEmail,
                    orderNumber,
                    order.CustomerName ?? FallbackCustomerName,
                    order.CustomerEmail ?? FallbackCustomerEmail,
                    order.CustomerPhone ?? FallbackPhone,
                    order.Type.ToString(),
                    order.Total,
                    items,
                    order.Notes,
                    deliveryAddress);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send admin notification email for order {OrderNumber}", orderNumber);
            }
        });
    }

    private static List<(string name, int quantity, decimal price)> ComposeEmailItems(OrderDto order) =>
        order.Items.Select(item => (
            name: $"{item.ProductName}{(string.IsNullOrEmpty(item.VariationName) ? "" : $" - {item.VariationName}")}",
            quantity: item.Quantity,
            price: item.ItemTotal
        )).ToList();

    private static string? ComposeDeliveryAddress(OrderDto order)
    {
        if (order.DeliveryAddress == null)
        {
            return null;
        }

        var address = $"{order.DeliveryAddress.AddressLine1}, " +
            $"{order.DeliveryAddress.PostalCode} {order.DeliveryAddress.City}, " +
            $"{order.DeliveryAddress.Country}";

        if (!string.IsNullOrEmpty(order.DeliveryAddress.DeliveryInstructions))
        {
            address += $"\n\nDelivery Instructions: {order.DeliveryAddress.DeliveryInstructions}";
        }

        return address;
    }

    // The DineInDefaultPrepMinutes const is exposed for the handler's
    // Dine-in code path so the value lives next to the email logic that
    // depends on it. Promote to OrderSettings:DineInDefaultPrepMinutes
    // if this needs to vary per deployment.
    public const int DefaultDineInPreparationMinutes = DineInDefaultPrepMinutes;
}
