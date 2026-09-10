using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderOperationalNoteCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrderOperationalNotesQuery;

namespace RestaurantSystem.Api.Features.Orders;

/// <summary>Staff-only, append-only operational notes for an order.</summary>
[ApiController]
[Route("api/orders/{orderId}/notes")]
public class OrderOperationalNotesController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public OrderOperationalNotesController(CustomMediator mediator) => _mediator = mediator;

    [HttpGet]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<List<OrderOperationalNoteDto>>>?> Get(Guid orderId)
        => Ok(await _mediator.SendQuery(new GetOrderOperationalNotesQuery(orderId)));

    [HttpPost]
    [RequireAdminOrCashier]
    public async Task<ActionResult<ApiResponse<OrderOperationalNoteDto>>> Create(
        Guid orderId,
        [FromBody] CreateOrderOperationalNoteCommand command)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command));
    }
}
