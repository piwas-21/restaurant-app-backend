using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCashierOrderGroupsQuery;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/orders/cashier-groups")]
[RequireStaff]
[RequireModule(ModuleIds.Cashier)]
public sealed class CashierOrderGroupsController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CashierOrderGroupDto>>>> GetGroups(
        [FromQuery] GetOrdersQuery filters) =>
        Ok(await mediator.SendQuery(new GetCashierOrderGroupsQuery(filters)));
}
