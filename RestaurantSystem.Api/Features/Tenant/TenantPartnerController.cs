using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Partner;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Tenant;

/// <summary>Public footer attribution, refreshed from the control plane without a restart.</summary>
[ApiController]
[Route("api/tenant")]
public class TenantPartnerController : ControllerBase
{
    private readonly ITenantBranding _branding;
    public TenantPartnerController(ITenantBranding branding) => _branding = branding;

    [HttpGet("partner")]
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TenantPartnerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TenantPartnerDto>>> GetPartner(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<TenantPartnerDto>.SuccessWithData(await _branding.GetAsync(cancellationToken)));
    }
}
