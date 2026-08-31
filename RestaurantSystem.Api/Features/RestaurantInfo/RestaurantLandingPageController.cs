using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateLandingPageCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Features.RestaurantInfo.Queries.GetLandingPageQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

/// <summary>
/// The landing page as its own contract, on its own controller: the profile PUT is a full
/// replace of the restaurant's address/identity row, while the landing page is background
/// mode + per-language copy, read anonymously by every tenant frontend. Sharing a route with
/// the profile would couple two payloads with different audiences and different validators.
/// </summary>
[ApiController]
[Route("api/restaurant-info/landing")]
public class RestaurantLandingPageController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public RestaurantLandingPageController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Public landing-page data, kept separate from the restaurant profile contract.</summary>
    [HttpGet]
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LandingPageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<LandingPageDto>>> GetLanding()
    {
        var result = await _mediator.SendQuery(new GetLandingPageQuery());
        return Ok(result);
    }

    /// <summary>Fully replaces the landing-page overrides and background mode.</summary>
    [HttpPut]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<LandingPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<LandingPageDto>>> UpdateLanding(
        [FromBody] UpdateLandingPageCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }
}
