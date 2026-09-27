using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Commands.SetMenuOfferParentCommand;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Menus;

public partial class MenusController
{
    /// <summary>Link a menu bundle to one product/variation offer without rewriting its sections.</summary>
    [HttpPatch("{id}/offer-parent")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<MenuOfferLinkDto>>> SetOfferParent(
        Guid id,
        [FromBody] MenuOfferParentRequestDto request)
    {
        var command = new SetMenuOfferParentCommand(
            id, request.ParentOfferProductId, request.ParentOfferVariationId);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>Clear a menu's offer-family relationship while retaining the menu itself.</summary>
    [HttpDelete("{id}/offer-parent")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<MenuOfferLinkDto>>> ClearOfferParent(Guid id)
    {
        var result = await _mediator.SendCommand(
            new SetMenuOfferParentCommand(id, ParentOfferProductId: null));
        return Ok(result);
    }
}
