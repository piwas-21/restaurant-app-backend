using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CompleteAllTableOrdersCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Commands.ToggleFocusOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.ApproveDelayCommand;
using RestaurantSystem.Api.Features.Orders.Commands.RejectDelayCommand;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetFocusOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Settings;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly ILogger<OrdersController> _logger;
    private readonly EmailSettings _emailSettings;

    public OrdersController(
        CustomMediator mediator,
        ILogger<OrdersController> logger,
        IOptions<EmailSettings> emailSettings)
    {
        _mediator = mediator;
        _logger = logger;
        _emailSettings = emailSettings.Value;
    }

    /// <summary>
    /// Get all orders with optional filters
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetOrders(
        [FromQuery] GetOrdersQuery query)
    {
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get Z-Report (end-of-day financial summary) for a specific date.
    /// Date is interpreted as a calendar day in UTC; the report covers
    /// [date 00:00 UTC, date+1 00:00 UTC). Defaults to today (UTC) if omitted.
    /// </summary>
    [HttpGet("z-report")]
    [RequireAdminOrCashier]
    public async Task<ActionResult<ApiResponse<ZReportDto>>> GetZReport([FromQuery] DateOnly? date)
    {
        var reportDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.SendQuery(new GetZReportQuery(reportDate));
        return Ok(result);
    }

    // Printer-feed endpoint moved to PrinterFeedController as part of the
    // OrdersController god-class decomposition (Sprint 2 task 2.3). The
    // route URL (api/orders/printer-feed) is preserved.

    /// <summary>
    /// Get order by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrder(Guid id)
    {
        var query = new GetOrderByIdQuery(id);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Create a new order with multiple payment options
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Add a payment to an existing order
    /// </summary>
    [HttpPost("{orderId}/payments")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> AddPayment(
        Guid orderId,
        [FromBody] AddPaymentToOrderCommand command)
    {
        command.OrderId = orderId;
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Toggle focus order status
    /// </summary>
    [HttpPut("{orderId}/focus")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> ToggleFocusOrder(
        Guid orderId,
        [FromBody] ToggleFocusOrderCommand command)
    {
        command.OrderId = orderId;
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Get all focus orders
    /// </summary>
    [HttpGet("focus")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetFocusOrders(
        [FromQuery] GetFocusOrdersQuery query)
    {
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Update order status
    /// </summary>
    [HttpPut("{orderId}/status")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(
        Guid orderId,
        [FromBody] UpdateOrderStatusCommand command)
    {
        command.OrderId = orderId;
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Cancel an order
    /// </summary>
    [HttpPost("{orderId}/cancel")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CancelOrder(
        Guid orderId,
        [FromBody] CancelOrderCommand command)
    {
        command.OrderId = orderId;
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Complete all active orders for a table (Admin and Server only)
    /// Intelligently transitions orders based on their status:
    /// - Ready orders are marked as Completed
    /// - Non-ready orders (Pending, Confirmed, Preparing) are Cancelled
    /// </summary>
    [HttpPost("table/{tableNumber}/complete-all")]
    [Authorize(Roles = "Admin,Server")]
    public async Task<ActionResult<ApiResponse<CompleteAllTableOrdersResult>>> CompleteAllTableOrders(
        string tableNumber)
    {
        var command = new CompleteAllTableOrdersCommand(tableNumber);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Refund a payment
    /// </summary>
    [HttpPost("{orderId}/payments/{paymentId}/refund")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OrderPaymentDto>>> RefundPayment(
        Guid orderId,
        Guid paymentId,
        [FromBody] RefundPaymentCommand command)
    {
        command.OrderId = orderId;
        command.PaymentId = paymentId;
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete an order (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteOrder(Guid id)
    {
        var command = new DeleteOrderCommand(id);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    // SendOrderConfirmationEmail moved to OrderEmailController as part of
    // the Sprint 2 god-class decomposition (task 2.4).

    /// <summary>
    /// Quick confirm order from email link
    /// </summary>
    [HttpGet("{orderNumber}/quick-confirm")]
    [AllowAnonymous]
    public async Task<IActionResult> QuickConfirmOrder(string orderNumber, [FromQuery] int minutes = 15)
    {
        try
        {
            // Find order by order number
            var orders = await _mediator.SendQuery(new GetOrdersQuery(
                Status: null,
                PaymentStatus: null,
                OrderType: null,
                StartDate: null,
                EndDate: null,
                UserId: null,
                Search: orderNumber,
                IsFocusOrder: null,
                OrderBy: "OrderDate",
                Descending: true,
                Page: 1,
                PageSize: 1
            ));
            var order = orders.Data?.Items.FirstOrDefault();

            if (order == null)
            {
                return Content($@"
                    <html>
                    <head><title>Order Not Found</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>❌ Order Not Found</h2>
                        <p>Order {orderNumber} could not be found.</p>
                    </body>
                    </html>", "text/html");
            }

            if (order.Status != "Pending")
            {
                return Content($@"
                    <html>
                    <head><title>Order Already Processed</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>ℹ️ Order Already Processed</h2>
                        <p>Order {orderNumber} has already been {order.Status.ToLower()}.</p>
                        <p>Current status: <strong>{order.Status}</strong></p>
                    </body>
                    </html>", "text/html");
            }

            // Define delay threshold - orders with prep time > this need customer approval
            const int delayThresholdMinutes = 10;

            // Determine status based on preparation time
            // If delay is significant, ask customer for approval first
            var newStatus = minutes > delayThresholdMinutes
                ? Domain.Common.Enums.OrderStatus.PendingApproval
                : Domain.Common.Enums.OrderStatus.Confirmed;

            var statusNote = minutes > delayThresholdMinutes
                ? $"Pending customer approval for {minutes} min preparation time"
                : $"Confirmed via email with {minutes} min preparation time";

            // Update order status
            var command = new UpdateOrderStatusCommand
            {
                OrderId = order.Id,
                NewStatus = newStatus,
                EstimatedPreparationMinutes = minutes,
                Notes = statusNote
            };

            var result = await _mediator.SendCommand(command);

            if (result.Success)
            {
                // Different messages based on whether it's immediate confirmation or pending approval
                var (title, icon, color, heading, message) = minutes > delayThresholdMinutes
                    ? ("Pending Customer Approval", "⏳", "#f59e0b", "Awaiting Customer Approval",
                       $"Order <strong>{orderNumber}</strong> requires customer approval for the {minutes}-minute preparation time.<br><br>The customer will receive an email to approve or reject this delay.")
                    : ("Order Confirmed", "✓", "#059669", "Order Confirmed!",
                       $"Order <strong>{orderNumber}</strong> has been confirmed.<br><br>Preparation time: <strong>{minutes} minutes</strong>");

                var frontendUrl = _emailSettings.FrontendBaseUrl;
                return Content($@"
                    <html>
                    <head>
                        <title>{title}</title>
                        <meta http-equiv='refresh' content='5;url={frontendUrl}/admin/orders-management'>
                    </head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <div style='max-width: 500px; margin: 0 auto;'>
                            <div style='font-size: 60px; color: {color}; margin-bottom: 20px;'>{icon}</div>
                            <h2 style='color: {color};'>{heading}</h2>
                            <p>{message}</p>
                            <p style='color: #666; font-size: 14px; margin-top: 30px;'>
                                The customer will be notified automatically.
                            </p>
                            <p style='color: #666; font-size: 12px; margin-top: 20px;'>
                                Redirecting to dashboard in 5 seconds...
                            </p>
                        </div>
                    </body>
                    </html>", "text/html");
            }
            else
            {
                return Content($@"
                    <html>
                    <head><title>Confirmation Failed</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>❌ Confirmation Failed</h2>
                        <p>{result.Message}</p>
                    </body>
                    </html>", "text/html");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quick confirm order {OrderNumber}", orderNumber);
            return Content($@"
                <html>
                <head><title>Error</title></head>
                <body style='font-family: Arial; text-align: center; padding: 50px;'>
                    <h2>❌ Error</h2>
                    <p>An error occurred while confirming the order.</p>
                    <p style='color: #666; font-size: 12px;'>{ex.Message}</p>
                </body>
                </html>", "text/html");
        }
    }

    /// <summary>
    /// Quick cancel order from email link
    /// </summary>
    [HttpGet("{orderNumber}/quick-cancel")]
    [AllowAnonymous]
    public async Task<IActionResult> QuickCancelOrder(string orderNumber)
    {
        try
        {
            // Find order by order number
            var orders = await _mediator.SendQuery(new GetOrdersQuery(
                Status: null,
                PaymentStatus: null,
                OrderType: null,
                StartDate: null,
                EndDate: null,
                UserId: null,
                Search: orderNumber,
                IsFocusOrder: null,
                OrderBy: "OrderDate",
                Descending: true,
                Page: 1,
                PageSize: 1
            ));
            var order = orders.Data?.Items.FirstOrDefault();

            if (order == null)
            {
                return Content($@"
                    <html>
                    <head><title>Order Not Found</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>❌ Order Not Found</h2>
                        <p>Order {orderNumber} could not be found.</p>
                    </body>
                    </html>", "text/html");
            }

            if (order.Status == "Cancelled")
            {
                return Content($@"
                    <html>
                    <head><title>Order Already Cancelled</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>ℹ️ Order Already Cancelled</h2>
                        <p>Order {orderNumber} has already been cancelled.</p>
                    </body>
                    </html>", "text/html");
            }

            if (order.Status != "Pending")
            {
                return Content($@"
                    <html>
                    <head><title>Cannot Cancel Order</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>⚠️ Cannot Cancel Order</h2>
                        <p>Order {orderNumber} cannot be cancelled because it is already {order.Status.ToLower()}.</p>
                        <p>Current status: <strong>{order.Status}</strong></p>
                    </body>
                    </html>", "text/html");
            }

            // Cancel the order
            var command = new CancelOrderCommand
            {
                OrderId = order.Id,
                CancellationReason = "Cancelled by admin via email"
            };

            var result = await _mediator.SendCommand(command);

            if (result.Success)
            {
                var frontendUrl = _emailSettings.FrontendBaseUrl;
                return Content($@"
                    <html>
                    <head>
                        <title>Order Cancelled</title>
                        <meta http-equiv='refresh' content='3;url={frontendUrl}/admin/orders-management'>
                    </head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <div style='max-width: 500px; margin: 0 auto;'>
                            <div style='font-size: 60px; color: #dc2626; margin-bottom: 20px;'>✕</div>
                            <h2 style='color: #dc2626;'>Order Cancelled</h2>
                            <p>Order <strong>{orderNumber}</strong> has been cancelled.</p>
                            <p style='color: #666; font-size: 14px; margin-top: 30px;'>
                                The customer will be notified about the cancellation.
                            </p>
                            <p style='color: #666; font-size: 12px; margin-top: 20px;'>
                                Redirecting to dashboard in 3 seconds...
                            </p>
                        </div>
                    </body>
                    </html>", "text/html");
            }
            else
            {
                return Content($@"
                    <html>
                    <head><title>Cancellation Failed</title></head>
                    <body style='font-family: Arial; text-align: center; padding: 50px;'>
                        <h2>❌ Cancellation Failed</h2>
                        <p>{result.Message}</p>
                    </body>
                    </html>", "text/html");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quick cancel order {OrderNumber}", orderNumber);
            return Content($@"
                <html>
                <head><title>Error</title></head>
                <body style='font-family: Arial; text-align: center; padding: 50px;'>
                    <h2>❌ Error</h2>
                    <p>An unexpected error occurred.</p>
                </body>
                </html>", "text/html");
        }
    }

    [HttpGet("{id}/approve-delay")]
    [AllowAnonymous]
    public async Task<IActionResult> ApproveDelay(Guid id)
    {
        try
        {
            var command = new ApproveDelayCommand(id);
            var result = await _mediator.SendCommand(command);

            if (result.Success)
            {
                return Content($@"
                    <html>
                    <head>
                        <title>Delay Approved</title>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    </head>
                    <body style='font-family: Arial; text-align: center; padding: 50px; background-color: #f9f9f9;'>
                        <div style='max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'>
                            <div style='font-size: 60px; color: #10b981; margin-bottom: 20px;'>✓</div>
                            <h2 style='color: #10b981;'>Delay Approved</h2>
                            <p>Thank you! Your order has been confirmed with the new preparation time.</p>
                            <p>We're getting started on your delicious meal right away!</p>
                        </div>
                    </body>
                    </html>", "text/html");
            }
            else
            {
                return Content($@"
                    <html>
                    <head>
                        <title>Action Failed</title>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    </head>
                    <body style='font-family: Arial; text-align: center; padding: 50px; background-color: #f9f9f9;'>
                        <div style='max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'>
                            <div style='font-size: 60px; color: #ef4444; margin-bottom: 20px;'>❌</div>
                            <h2>Action Failed</h2>
                            <p>{result.Message}</p>
                        </div>
                    </body>
                    </html>", "text/html");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve delay for order {OrderId}", id);
            return Content("An error occurred while processing your request.", "text/plain");
        }
    }

    [HttpGet("{id}/reject-delay")]
    [AllowAnonymous]
    public async Task<IActionResult> RejectDelay(Guid id)
    {
        try
        {
            var command = new RejectDelayCommand(id);
            var result = await _mediator.SendCommand(command);

            if (result.Success)
            {
                return Content($@"
                    <html>
                    <head>
                        <title>Order Cancelled</title>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    </head>
                    <body style='font-family: Arial; text-align: center; padding: 50px; background-color: #f9f9f9;'>
                        <div style='max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'>
                            <div style='font-size: 60px; color: #ef4444; margin-bottom: 20px;'>✕</div>
                            <h2 style='color: #ef4444;'>Order Cancelled</h2>
                            <p>We've received your request to cancel the order.</p>
                            <p>You will not be charged for this order.</p>
                            <p>We hope to serve you again in the future!</p>
                        </div>
                    </body>
                    </html>", "text/html");
            }
            else
            {
                return Content($@"
                    <html>
                    <head>
                        <title>Action Failed</title>
                        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                    </head>
                    <body style='font-family: Arial; text-align: center; padding: 50px; background-color: #f9f9f9;'>
                        <div style='max-width: 500px; margin: 0 auto; background: white; padding: 40px; border-radius: 10px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'>
                            <div style='font-size: 60px; color: #ef4444; margin-bottom: 20px;'>❌</div>
                            <h2>Action Failed</h2>
                            <p>{result.Message}</p>
                        </div>
                    </body>
                    </html>", "text/html");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reject delay for order {OrderId}", id);
            return Content("An error occurred while processing your request.", "text/plain");
        }
    }
}
