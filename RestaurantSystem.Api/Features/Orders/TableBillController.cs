using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetTableBillQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Orders;

// ONE bill per table (waiter/POS): read the union of the table's open orders,
// settle it with one tender. Split out of OrdersController at birth to keep that
// controller under its length budget — the same decomposition Sprint 2 applied
// to the printer feed, order emails and quick actions.
//
// Table-service staff only on both endpoints ([RequireTableServiceStaff]): the bill exposes the
// table's WHOLE spend, and the payment is a till action. Kitchen staff do not need a table-wide
// receipt or tender path. The guest surfaces keep seeing only their own orders.
[ApiController]
[Route("api/orders/table")]
public class TableBillController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public TableBillController(CustomMediator mediator) => _mediator = mediator;

    /// <summary>
    /// ONE bill for a dine-in table: every order the table placed this service
    /// (still open), grouped per order, with bill-level sums.
    /// </summary>
    [HttpGet("{tableNumber}/bill")]
    [Authorize]
    [RequireTableServiceStaff]
    public async Task<ActionResult<ApiResponse<TableBillDto>>> GetTableBill(int tableNumber)
        => Ok(await _mediator.SendQuery(new GetTableBillQuery(tableNumber)));

    /// <summary>
    /// ONE tender for the table's whole bill, spread across its open orders
    /// oldest-round-first. Over-tender is refused; a mid-allocation change fails
    /// the whole tender rather than half-settling it.
    /// </summary>
    [HttpPost("{tableNumber}/bill/payments")]
    [Authorize]
    [RequireTableServiceStaff]
    public async Task<ActionResult<ApiResponse<TableBillDto>>> AddTableBillPayment(
        int tableNumber, [FromBody] AddTableBillPaymentCommand command)
    {
        command.TableNumber = tableNumber;
        return Ok(await _mediator.SendCommand(command));
    }
}
