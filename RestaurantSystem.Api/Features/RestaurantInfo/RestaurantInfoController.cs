using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.AddPhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeletePhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdatePhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Features.RestaurantInfo.Queries.GetRestaurantInfoQuery;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

/// <summary>
/// Restaurant identity + contact details (singleton). Read endpoint is
/// public — the data is shown on the customer-facing footer / map / tap-
/// to-call links. Mutations require the Admin role.
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

    [HttpPut]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> Update(
        [FromBody] UpdateRestaurantInfoCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    [HttpPost("phones")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantPhoneNumberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantPhoneNumberDto>>> AddPhone(
        [FromBody] AddPhoneNumberCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    [HttpPut("phones/{id:guid}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantPhoneNumberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantPhoneNumberDto>>> UpdatePhone(
        Guid id, [FromBody] UpdatePhoneNumberCommand command)
    {
        // Route id wins over body id — the URL is the canonical identifier.
        var result = await _mediator.SendCommand(command with { Id = id });
        return Ok(result);
    }

    [HttpDelete("phones/{id:guid}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<Guid>>> DeletePhone(Guid id)
    {
        var result = await _mediator.SendCommand(new DeletePhoneNumberCommand(id));
        return Ok(result);
    }
}
