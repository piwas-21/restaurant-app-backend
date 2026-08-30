using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Partner;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Tenant;

/// <summary>
/// Publishes this instance's partner attribution so the customer-facing footer can render
/// "Site by &lt;partner&gt;" (workspace docs/plans/SOFRA-PARTNER-PLAN.md §11, slice S4a).
///
/// This endpoint exists for exactly the reason <c>GET /api/tenant/modules</c> does: the
/// frontend CANNOT read the value itself, because its own knobs are NEXT_PUBLIC_*, baked into
/// the per-tenant image at build time. Serving it here makes a partner correcting their brand
/// name a re-provision plus a backend restart instead of a build-tenant-image.yml re-run for
/// every tenant that reseller sold (§11d, channel C).
///
/// Anonymous by design, like the two sibling tenant endpoints — the footer is chrome that
/// renders before anyone logs in, and it reveals nothing the rendered page does not show.
///
/// DECISION — no attribution answers 200 with nulls, NOT 404. That is the case for every
/// tenant today, so it is normal operation rather than a missing resource. A 404 would be
/// indistinguishable from a tenant running an image older than this endpoint, and the footer
/// must degrade to nothing in both cases; giving the two states the same wire answer means the
/// client has ONE branch (name == null) instead of a status-code branch it could get wrong on
/// only one of the two. It also keeps the answer cacheable and off the error path — a 404 goes
/// through ExceptionHandlingMiddleware's log, and a normal tenant is not a fault to be logged
/// on every page view.
/// </summary>
[ApiController]
[Route("api/tenant")]
public class TenantPartnerController : ControllerBase
{
    private readonly ITenantPartner _partner;

    public TenantPartnerController(ITenantPartner partner)
    {
        _partner = partner;
    }

    /// <summary>The partner credited on this tenant's public pages, or nulls when there is none.</summary>
    [HttpGet("partner")]
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TenantPartnerDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<TenantPartnerDto>> GetPartner()
    {
        // The url is validated in TenantPartner, at the configuration boundary, not here: it is
        // fixed for the process lifetime, so re-deriving it per request would be the same answer
        // computed on every page view.
        var dto = new TenantPartnerDto(_partner.Name, _partner.Url);
        return Ok(ApiResponse<TenantPartnerDto>.SuccessWithData(dto));
    }
}
