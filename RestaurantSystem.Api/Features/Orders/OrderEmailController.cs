using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders;

// Email-confirmation endpoints split out from OrdersController as part of
// the Sprint 2 god-class decomposition (task 2.4). Behaviour is unchanged
// from the original action; the email-composition logic itself is slated
// to move into an OrderNotificationService in task 2.10.
[ApiController]
[Route("api/orders")]
public class OrderEmailController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly IEmailService _emailService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<OrderEmailController> _logger;

    public OrderEmailController(
        CustomMediator mediator,
        IEmailService emailService,
        IOptions<EmailSettings> emailSettings,
        ILogger<OrderEmailController> logger)
    {
        _mediator = mediator;
        _emailService = emailService;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Send order confirmation emails to customer and admin.
    /// </summary>
    [HttpPost("{orderId}/send-confirmation-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> SendOrderConfirmationEmail(Guid orderId)
    {
        try
        {
            var orderResult = await _mediator.SendQuery(new GetOrderByIdQuery(orderId));

            if (!orderResult.Success || orderResult.Data == null)
            {
                return BadRequest(ApiResponse<string>.Failure("Order not found"));
            }

            var order = orderResult.Data;

            var items = order.Items.Select(item => (
                name: $"{item.ProductName}{(string.IsNullOrEmpty(item.VariationName) ? "" : $" - {item.VariationName}")}",
                quantity: item.Quantity,
                price: item.ItemTotal
            )).ToList();

            string? deliveryAddress = null;
            if (order.DeliveryAddress != null)
            {
                deliveryAddress = $"{order.DeliveryAddress.AddressLine1}, " +
                    $"{order.DeliveryAddress.PostalCode} {order.DeliveryAddress.City}, " +
                    $"{order.DeliveryAddress.Country}";

                if (!string.IsNullOrEmpty(order.DeliveryAddress.DeliveryInstructions))
                {
                    deliveryAddress += $"\n\nDelivery Instructions: {order.DeliveryAddress.DeliveryInstructions}";
                }
            }

            await _emailService.SendOrderReceivedEmailAsync(
                order.CustomerEmail ?? "noemail@example.com",
                order.CustomerName ?? "Valued Customer",
                order.OrderNumber,
                order.Type.ToString(),
                order.Total,
                items,
                order.Notes,
                deliveryAddress);

            // Admin notification fired and forgotten — admin email failures
            // must not block the customer-facing success response.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendOrderConfirmationAdminEmailAsync(
                        _emailSettings.AdminEmail,
                        order.OrderNumber,
                        order.CustomerName ?? "Valued Customer",
                        order.CustomerEmail ?? "noemail@example.com",
                        order.CustomerPhone ?? "Not provided",
                        order.Type.ToString(),
                        order.Total,
                        items,
                        order.Notes,
                        deliveryAddress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send admin notification email for order {OrderNumber}", order.OrderNumber);
                }
            });

            return Ok(ApiResponse<string>.SuccessWithData("Order confirmation emails sent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order confirmation emails for order {OrderId}", orderId);
            return BadRequest(ApiResponse<string>.Failure($"Failed to send confirmation emails: {ex.Message}"));
        }
    }
}
