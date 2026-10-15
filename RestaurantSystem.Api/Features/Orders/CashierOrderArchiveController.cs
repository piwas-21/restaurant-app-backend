using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Commands.DismissPriorDayOrderReviewCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCashierOrderArchiveQuery;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/orders")]
public sealed class CashierOrderArchiveController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("cashier/archive")]
    [RequireAdminOrCashier]
    [RequireModule(ModuleIds.Cashier)]
    public async Task<ActionResult<ApiResponse<CashierOrderArchiveDto>>> GetCashierArchive(
        [FromQuery] GetCashierOrderArchiveQuery query)
        => Ok(await mediator.SendQuery(query));

    [HttpPost("{orderId}/prior-day-review/dismiss")]
    [RequireAdminOrCashier]
    [RequireModule(ModuleIds.Cashier)]
    public async Task<ActionResult<ApiResponse<bool>>> DismissPriorDayReview(Guid orderId)
        => Ok(await mediator.SendCommand(new DismissPriorDayOrderReviewCommand(orderId)));
}
