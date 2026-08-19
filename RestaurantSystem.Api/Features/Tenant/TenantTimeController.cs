using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Tenant.Dtos;

namespace RestaurantSystem.Api.Features.Tenant;

/// <summary>
/// Publishes what day it is at the restaurant, because a browser cannot work it out.
///
/// A device knows its own zone and nothing about the tenant's (<c>Localization:TimeZone</c>), so
/// every client-side "today" is the guest's day, not the venue's. Between local midnight and the
/// UTC one those are different days, and the consequences are not cosmetic: the till read
/// yesterday's takings (backend #372, frontend #511) and the reservation form offers — and books —
/// a day the server then refuses as past (backend #369, frontend #517).
///
/// Anonymous for the same reason <c>GET /api/tenant/modules</c> is: the reservation form needs it
/// before anyone logs in, and the answer is on the restaurant's own front door.
/// </summary>
[ApiController]
[Route("api/tenant")]
public class TenantTimeController : ControllerBase
{
    private readonly ITenantClock _clock;

    public TenantTimeController(ITenantClock clock)
    {
        _clock = clock;
    }

    /// <summary>The tenant's current calendar day, on the tenant's own wall clock.</summary>
    [HttpGet("today")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TenantTodayDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<TenantTodayDto>> GetToday()
    {
        // A cached day is a wrong day for up to 24 hours, and this is the one endpoint whose whole
        // value is being current — so it says so itself rather than trusting every proxy, browser
        // and service worker between here and the guest to guess.
        Response.Headers.CacheControl = "no-store";

        // The same derivation `OrdersController` defaults the Z-report's day with — one clock, one
        // rule, so the day this endpoint publishes and the day a report covers cannot drift.
        var dto = new TenantTodayDto(DateOnly.FromDateTime(_clock.Now.Date), _clock.TimeZone.Id);

        return Ok(ApiResponse<TenantTodayDto>.SuccessWithData(dto));
    }
}
