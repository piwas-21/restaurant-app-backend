using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.BillCharges.Commands;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Queries;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.BillCharges;

[ApiController]
[Route("api/bill-charges")]
[Authorize]
[RequireTableServiceStaff]
public sealed class BillChargesController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<BillChargeConfigurationDto>>> Get() =>
        Ok(await mediator.SendQuery(new GetBillChargeConfigurationQuery()));

    [HttpPut]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<BillChargeConfigurationDto>>> Update(UpdateBillChargeConfigurationCommand command) =>
        Ok(await mediator.SendCommand(command));

    [HttpPost("orders/{orderId:guid}")]
    [RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
    public async Task<ActionResult<ApiResponse<OrderDto>>> Add(Guid orderId, AddBillChargesRequest request) =>
        Ok(await mediator.SendCommand(new AddOrderBillChargesCommand(request) { OrderId = orderId }));
}
