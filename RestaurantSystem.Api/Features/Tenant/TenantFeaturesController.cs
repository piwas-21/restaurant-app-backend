using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Tenant;

/// <summary>
/// Publishes tenant rollout switches to the frontend shell.
/// </summary>
[ApiController]
[Route("api/tenant")]
public sealed class TenantFeaturesController : ControllerBase
{
    private readonly ITenantFeatures _features;

    public TenantFeaturesController(ITenantFeatures features)
    {
        _features = features;
    }

    [HttpGet("features")]
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TenantFeaturesDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<TenantFeaturesDto>> Get()
    {
        Response.Headers.CacheControl = "no-store";
        var dto = new TenantFeaturesDto(_features.ServerWorkspaceV2);
        return Ok(ApiResponse<TenantFeaturesDto>.SuccessWithData(dto));
    }
}
