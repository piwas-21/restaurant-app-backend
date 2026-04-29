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
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetFocusOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public OrdersController(CustomMediator mediator)
    {
        _mediator = mediator;
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

    // Routes moved out as part of the Sprint 2 god-class decomposition:
    //  - GET  /api/orders/printer-feed                          -> PrinterFeedController          (task 2.3)
    //  - POST /api/orders/{orderId}/send-confirmation-email     -> OrderEmailController           (task 2.4)
    //  - GET  /api/orders/{orderNumber}/quick-confirm           -> OrderQuickActionsController    (task 2.5)
    //  - GET  /api/orders/{orderNumber}/quick-cancel            -> OrderQuickActionsController    (task 2.5)
    //  - GET  /api/orders/{id}/approve-delay                    -> OrderQuickActionsController    (task 2.5)
    //  - GET  /api/orders/{id}/reject-delay                     -> OrderQuickActionsController    (task 2.5)
}
