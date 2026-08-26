using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.ApiTokens.Commands.CreateApiTokenCommand;
using RestaurantSystem.Api.Features.ApiTokens.Commands.RevokeApiTokenCommand;
using RestaurantSystem.Api.Features.ApiTokens.Dtos;
using RestaurantSystem.Api.Features.ApiTokens.Dtos.Requests;
using RestaurantSystem.Api.Features.ApiTokens.Queries.GetApiTokensQuery;

namespace RestaurantSystem.Api.Features.ApiTokens;

/// <summary>
/// Manage scoped credentials for machine clients — agents and scripts
/// (docs/plans/API-TOKENS-PLAN.md).
/// </summary>
/// <remarks>
/// Admin-only, and deliberately carries NO <c>[ApiScope]</c> anywhere: that absence is what makes
/// this controller unreachable BY a token, so a machine credential can never mint, read or revoke
/// another one — including itself. Do not add a scope here.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[RequireAdmin]
public class ApiTokensController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public ApiTokensController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List every token with its status. Never returns a plaintext or a hash.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ApiTokenDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ApiTokenDto>>>> GetTokens()
    {
        var result = await _mediator.SendQuery(new GetApiTokensQuery());
        return Ok(result);
    }

    /// <summary>
    /// Create a token. The plaintext is in the response and NOWHERE else — there is no endpoint
    /// that can show it again, by design.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreatedApiTokenDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<CreatedApiTokenDto>>> CreateToken(
        [FromBody] CreateApiTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _mediator.SendCommand(
            new CreateApiTokenCommand(request.Name, request.Scopes, request.ExpiresInDays));

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Revoke a token. Idempotent; takes effect on the holder's very next request.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeToken(Guid id)
    {
        var result = await _mediator.SendCommand(new RevokeApiTokenCommand(id));
        return Ok(result);
    }
}
