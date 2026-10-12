using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateTipPreferencesCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.RestaurantInfo;

[ApiController]
[Route("api/restaurant-info/tips")]
public sealed class RestaurantTipPreferencesController(CustomMediator mediator) : ControllerBase
{
    [HttpPut]
    [ApiScope(ApiTokenScopes.TenantWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<TipPreferencesDto>>> Update([FromBody] UpdateTipPreferencesCommand command)
        => Ok(await mediator.SendCommand(command));
}
