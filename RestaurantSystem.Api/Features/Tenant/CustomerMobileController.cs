using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;

namespace RestaurantSystem.Api.Features.Tenant;

[ApiController]
[Route("api/tenant/mobile")]
public sealed class CustomerMobileController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("bootstrap")]
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CustomerMobileBootstrapDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CustomerMobileBootstrapDto>>> Get(
        [FromQuery] int contractVersion = 1, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(contractVersion), cancellationToken);
        return Ok(result);
    }
}
