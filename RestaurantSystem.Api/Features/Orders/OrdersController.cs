using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CompleteAllTableOrdersCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Commands.ToggleFocusOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
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

    public OrdersController(CustomMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderDto>>>> GetOrders([FromQuery] GetOrdersQuery query)
        => Ok(await _mediator.SendQuery(query));

    // The till's day-close report — the `cashier` module's own surface (sofra ADR-010), so it is
    // module-gated here while the rest of this controller stays core order handling.
    // Date is interpreted as a calendar day in UTC; the report covers
    // [date 00:00 UTC, date+1 00:00 UTC). Defaults to today (UTC) if omitted.
    [HttpGet("z-report")]
    [RequireAdminOrCashier]
    [RequireModule(ModuleIds.Cashier)]
    public async Task<ActionResult<ApiResponse<ZReportDto>>> GetZReport([FromQuery] DateOnly? date)
        => Ok(await _mediator.SendQuery(new GetZReportQuery(date ?? DateOnly.FromDateTime(DateTime.UtcNow))));

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrder(Guid id)
        => Ok(await _mediator.SendQuery(new GetOrderByIdQuery(id)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderCommand command)
        => Ok(await _mediator.SendCommand(command));

    // Order-from-basket: the server reads the user's persisted basket and owns the basket→order
    // item translation (menu-bundles redesign #157), instead of the client hand-building Items.
    // Session comes from the header (as with the basket endpoints), never the request body.
    [HttpPost("from-basket")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrderFromBasket(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] CreateOrderFromBasketCommand command)
    {
        command.SessionId = sessionId;
        return Ok(await _mediator.SendCommand(command));
    }

    // §9.19 follow-up: [RequireStaff], not [Authorize]. Taking a payment is a till action — there is
    // no gateway behind this, the handler marks the payment Completed outright — so with only
    // "is authenticated" any customer could mark ANY order paid and mint its fidelity points.
    [HttpPost("{orderId}/payments")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> AddPayment(Guid orderId, [FromBody] AddPaymentToOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    // Focus is a kitchen/cashier triage flag. It also returned the full OrderDto for any order id,
    // which made it a read primitive for another customer's PII with a near-no-op write attached.
    [HttpPut("{orderId}/focus")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> ToggleFocusOrder(Guid orderId, [FromBody] ToggleFocusOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    // Returns every focused order with no ownership filter of any kind.
    [HttpGet("focus")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetFocusOrders([FromQuery] GetFocusOrdersQuery query)
        => Ok(await _mediator.SendQuery(query));

    // Drives the kitchen/cashier state machine and sends customer emails on some transitions.
    [HttpPut("{orderId}/status")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(Guid orderId, [FromBody] UpdateOrderStatusCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    // Cancelling force-refunds every completed payment on the order and emails the customer. Staff
    // only, which matches the product as it exists: no customer surface calls this (verified across
    // the frontend — cashier and admin only). If "cancel my own order" is ever wanted, the shape is
    // an owner-or-staff test in the HANDLER, as GetOrderByIdQuery does — not a return to [Authorize].
    [HttpPost("{orderId}/cancel")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CancelOrder(Guid orderId, [FromBody] CancelOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    // Intelligently transitions orders by current status:
    //   Ready                       -> Completed
    //   Pending / Confirmed / Preparing -> Cancelled
    [HttpPost("table/{tableNumber}/complete-all")]
    [Authorize(Roles = "Admin,Server")]
    public async Task<ActionResult<ApiResponse<CompleteAllTableOrdersResult>>> CompleteAllTableOrders(string tableNumber)
        => Ok(await _mediator.SendCommand(new CompleteAllTableOrdersCommand(tableNumber)));

    [HttpPost("{orderId}/payments/{paymentId}/refund")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OrderPaymentDto>>> RefundPayment(Guid orderId, Guid paymentId, [FromBody] RefundPaymentCommand command)
    {
        command.OrderId = orderId;
        command.PaymentId = paymentId;
        return Ok(await _mediator.SendCommand(command));
    }

    [HttpDelete("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteOrder(Guid id)
        => Ok(await _mediator.SendCommand(new DeleteOrderCommand(id)));

    // Routes moved out as part of the Sprint 2 god-class decomposition:
    //   /api/orders/printer-feed                  -> PrinterFeedController        (task 2.3)
    //   /api/orders/{id}/send-confirmation-email  -> OrderEmailController         (task 2.4)
    //   /api/orders/{n}/quick-confirm|quick-cancel
    //   /api/orders/{id}/approve-delay|reject-delay
    //                                             -> OrderQuickActionsController  (task 2.5)
}
