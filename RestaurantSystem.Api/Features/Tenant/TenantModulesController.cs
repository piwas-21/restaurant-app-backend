using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Tenant;

/// <summary>
/// Publishes this instance's module set so the frontend can gate routes and nav
/// (sofra ADR-010 / S11).
///
/// This endpoint exists because the frontend CANNOT read the flags itself: its own
/// knobs are NEXT_PUBLIC_*, baked into the per-tenant image at build time, and the
/// tenant compose hands TENANT_MODULES to the backend service only. Serving them here
/// makes a module upgrade a re-provision plus a backend restart instead of a
/// build-tenant-image.yml re-run.
///
/// Anonymous by design — the customer-facing chrome needs it before anyone logs in,
/// and it reveals nothing the rendered UI does not already show.
/// </summary>
[ApiController]
[Route("api/tenant")]
public class TenantModulesController : ControllerBase
{
    private readonly ITenantModules _modules;

    public TenantModulesController(ITenantModules modules)
    {
        _modules = modules;
    }

    [HttpGet("modules")]
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TenantModulesDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<TenantModulesDto>> Get()
    {
        var dto = new TenantModulesDto(_modules.EnabledModules, _modules.IsEnforced);
        return Ok(ApiResponse<TenantModulesDto>.SuccessWithData(dto));
    }
}
