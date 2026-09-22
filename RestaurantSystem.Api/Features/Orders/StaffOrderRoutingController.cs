using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders;

/// <summary>Read-only routing projection for staff-visible counter orders.</summary>
[ApiController]
[Route("api/staff/orders")]
[Authorize]
[RequireTableServiceStaff]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
public sealed class StaffOrderRoutingController : ControllerBase
{
    private readonly IOrderRoutingService _routing;

    public StaffOrderRoutingController(IOrderRoutingService routing) => _routing = routing;

    /// <summary>Returns the authoritative route lifecycle for a staff-visible order.</summary>
    [HttpGet("{orderId:guid}/routing")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderRoutingStateDto>>>> Routing(
        Guid orderId, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<OrderRoutingStateDto>>.SuccessWithData(
            await _routing.ProjectAsync(orderId, cancellationToken), "Routing state loaded."));
}
