using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders;

// Email-confirmation endpoint split out from OrdersController in
// Sprint 2 task 2.4. Composition of the email body now lives in
// IOrderNotificationService (task 2.10); this controller is a thin
// dispatcher.
[ApiController]
[Route("api/orders")]
public class OrderEmailController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly IOrderNotificationService _notifications;
    private readonly ILogger<OrderEmailController> _logger;

    public OrderEmailController(
        CustomMediator mediator,
        IOrderNotificationService notifications,
        ILogger<OrderEmailController> logger)
    {
        _mediator = mediator;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Send order confirmation emails to customer and admin.
    /// </summary>
    [HttpPost("{orderId}/send-confirmation-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> SendOrderConfirmationEmail(Guid orderId)
    {
        var orderResult = await _mediator.SendQuery(new GetOrderByIdQuery(orderId));
        if (!orderResult.Success || orderResult.Data == null)
        {
            return BadRequest(ApiResponse<string>.Failure("Order not found"));
        }

        try
        {
            await _notifications.SendOrderConfirmationAsync(orderResult.Data);
            return Ok(ApiResponse<string>.SuccessWithData("Order confirmation emails sent successfully"));
        }
        catch (Exception ex)
        {
            // Generic message — `ex.Message` can leak SMTP server details.
            // Full exception logged server-side; the client gets a stable
            // surface. Closes part of issue #13.
            _logger.LogError(ex, "Failed to send order confirmation emails for order {OrderId}", orderId);
            return BadRequest(ApiResponse<string>.Failure("Failed to send confirmation emails"));
        }
    }
}
