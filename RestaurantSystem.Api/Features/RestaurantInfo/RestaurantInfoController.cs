using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.AddPhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeletePhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeleteRestaurantInteriorImageCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeleteRestaurantLogoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdatePhoneNumberCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInteriorImageCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantLogoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos.Requests;
using RestaurantSystem.Api.Features.RestaurantInfo.Queries.GetRestaurantInfoQuery;
using RestaurantSystem.Domain.Common.Constants;

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
    [ApiScope(ApiTokenScopes.TenantRead)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> Get()
    {
        var result = await _mediator.SendQuery(new GetRestaurantInfoQuery());
        return Ok(result);
    }

    [HttpPut]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> Update(
        [FromBody] UpdateRestaurantInfoCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Replace one of the restaurant's logos. Clearing it is a DELETE, not an empty upload —
    /// "no logo" is a real state (the app then renders the restaurant's name as text).
    /// </summary>
    [HttpPut("logo/{variant}")]
    [Consumes("multipart/form-data")]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> UpdateLogo(
        LogoVariant variant,
        [FromForm] UpdateRestaurantLogoRequest request)
    {
        var result = await _mediator.SendCommand(
            new UpdateRestaurantLogoCommand(variant, request.Logo));
        return Ok(result);
    }

    [HttpDelete("logo/{variant}")]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> DeleteLogo(LogoVariant variant)
    {
        var result = await _mediator.SendCommand(new DeleteRestaurantLogoCommand(variant));
        return Ok(result);
    }

    /// <summary>Replace the uploaded image available to custom landing-background mode.
    /// Removing it is a DELETE; default and none background modes remain available.</summary>
    [HttpPut("interior-image")]
    [Consumes("multipart/form-data")]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> UpdateInteriorImage(
        [FromForm] UpdateRestaurantInteriorImageRequest request)
    {
        var result = await _mediator.SendCommand(
            new UpdateRestaurantInteriorImageCommand(request.Image));
        return Ok(result);
    }

    [HttpDelete("interior-image")]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<RestaurantInfoDto>>> DeleteInteriorImage()
    {
        var result = await _mediator.SendCommand(new DeleteRestaurantInteriorImageCommand());
        return Ok(result);
    }

    [HttpPost("phones")]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<RestaurantPhoneNumberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<RestaurantPhoneNumberDto>>> AddPhone(
        [FromBody] AddPhoneNumberCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    [HttpPut("phones/{id:guid}")]
    [ApiScope(ApiTokenScopes.TenantWrite)]
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
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<Guid>>> DeletePhone(Guid id)
    {
        var result = await _mediator.SendCommand(new DeletePhoneNumberCommand(id));
        return Ok(result);
    }
}
