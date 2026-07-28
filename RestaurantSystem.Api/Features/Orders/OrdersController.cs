using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
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

    // Date is interpreted as a calendar day in UTC; the report covers
    // [date 00:00 UTC, date+1 00:00 UTC). Defaults to today (UTC) if omitted.
    [HttpGet("z-report")]
    [RequireAdminOrCashier]
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

    // ── Back-of-house order operations ──────────────────────────────────
    // These five were [Authorize]-only while their handlers loaded the order by id alone,
    // so any authenticated customer could drive another customer's order and take back a
    // full OrderDto (name, email, phone, address, payments). No customer surface calls them.
    //
    // The gate is the attribute, NOT a check inside the handlers: OrderQuickActions-
    // Controller dispatches UpdateOrderStatusCommand and CancelOrderCommand from
    // [AllowAnonymous] email-link actions, where a handler-level IsStaff check would see
    // no user at all and break those links.
    //
    // [RequireStaff] is the attribute form of ICurrentUserService.IsStaff — the same four
    // roles on purpose, so route gate and ownership predicate cannot drift. A 403 here
    // leaks no ids: the attribute rejects before any DB lookup.

    // Admin/Cashier rather than all staff: this writes money, and belongs to the till
    // cluster (z-report above, refund below). No server/kitchen surface takes payment.
    [HttpPost("{orderId}/payments")]
    [RequireAdminOrCashier]
    public async Task<ActionResult<ApiResponse<OrderDto>>> AddPayment(Guid orderId, [FromBody] AddPaymentToOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    [HttpPut("{orderId}/focus")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> ToggleFocusOrder(Guid orderId, [FromBody] ToggleFocusOrderCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    // Unfiltered by owner by design — the expo/priority queue over every order, so it is
    // staff-only rather than scoped the way the customer order list is.
    [HttpGet("focus")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<List<OrderDto>>>> GetFocusOrders([FromQuery] GetFocusOrdersQuery query)
        => Ok(await _mediator.SendQuery(query));

    [HttpPut("{orderId}/status")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> UpdateOrderStatus(Guid orderId, [FromBody] UpdateOrderStatusCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }

    // No owner branch: no customer surface cancels an order, and the customer-initiated
    // path is the emailed reject-delay link, which is anonymous and reaches
    // RejectDelayCommand instead. An owner branch here would be unreachable code.
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
