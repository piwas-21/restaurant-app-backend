using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Features.RestaurantInfo.Queries.GetRestaurantInfoQuery;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

/// <summary>
/// Restaurant identity + contact details (singleton). Read endpoint is
/// public — the data is shown on the customer-facing footer / map / tap-
/// to-call links. Mutation endpoints land in a follow-up MR.
/// </summary>
[ApiController]
[Route("api/restaurant-info")]
public class RestaurantInfoController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public RestaurantInfoController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> Get()
    {
        var result = await _mediator.SendQuery(new GetRestaurantInfoQuery());
        return Ok(result);
    }
}
