using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.ApproveOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/orders")]
public sealed class OrderApprovalController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public OrderApprovalController(CustomMediator mediator) => _mediator = mediator;

    [HttpPost("{orderId:guid}/approve"), ApiScope(ApiTokenScopes.OrdersWrite)]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Approve(
        Guid orderId,
        [FromBody] ApproveOrderCommand command,
        CancellationToken cancellationToken)
    {
        command.OrderId = orderId;
        return Ok(await _mediator.SendCommand(command, cancellationToken));
    }
}
