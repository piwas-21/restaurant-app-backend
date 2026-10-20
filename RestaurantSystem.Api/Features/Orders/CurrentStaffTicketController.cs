using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCurrentStaffTicketQuery;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[Route("api/orders/{orderId:guid}/current-ticket")]
[RequireStaff]
[RequireModule(ModuleIds.Cashier, ModuleIds.Server)]
public sealed class CurrentStaffTicketController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderDto>>>> Get(Guid orderId) =>
        Ok(await mediator.SendQuery(new GetCurrentStaffTicketQuery(orderId)));
}
