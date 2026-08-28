using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalVariations.Commands.CreateGlobalVariationCommand;
using RestaurantSystem.Api.Features.GlobalVariations.Commands.DeleteGlobalVariationCommand;
using RestaurantSystem.Api.Features.GlobalVariations.Commands.RestoreGlobalVariationCommand;
using RestaurantSystem.Api.Features.GlobalVariations.Commands.UpdateGlobalVariationCommand;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.GlobalVariations.Queries.GetGlobalVariationByIdQuery;
using RestaurantSystem.Api.Features.GlobalVariations.Queries.GetGlobalVariationsQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.GlobalVariations;

/// <summary>
/// The variation library (plan S4). The surface mirrors <c>GlobalIngredientsController</c> minus one
/// endpoint: there is no <c>/search</c>, because the ingredient one cannot browse and matches only
/// the English default name, so the picker reads the whole list — see
/// <c>GetGlobalVariationsQueryHandler</c>.
/// </summary>
[ApiController]
[Route("api/global-variations")]
public class GlobalVariationsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public GlobalVariationsController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<GlobalVariationDto>>>> GetGlobalVariations() =>
        Ok(await _mediator.SendQuery(new GetGlobalVariationsQuery()));

    /// <summary>
    /// The archive drawer — admin only, because it exists to undo an admin action. The literal
    /// segment wins route precedence over the <c>{id}</c> route below, which is why it is first.
    /// </summary>
    [HttpGet("archived")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<List<GlobalVariationDto>>>> GetArchivedGlobalVariations() =>
        Ok(await _mediator.SendQuery(new GetGlobalVariationsQuery(ArchivedOnly: true)));

    [HttpGet("{id}")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GlobalVariationDto>>> GetGlobalVariation(Guid id) =>
        Ok(await _mediator.SendQuery(new GetGlobalVariationByIdQuery(id)));

    [HttpPost]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalVariationDto>>> CreateGlobalVariation(
        [FromBody] CreateGlobalVariationDto body) =>
        Ok(await _mediator.SendCommand(new CreateGlobalVariationCommand(body.DefaultName, body.Translations)));

    [HttpPut("{id}")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalVariationDto>>> UpdateGlobalVariation(
        Guid id,
        [FromBody] UpdateGlobalVariationDto body) =>
        Ok(await _mediator.SendCommand(new UpdateGlobalVariationCommand(
            id, body.DefaultName, body.IsActive, body.Translations)));

    [HttpPost("{id}/restore")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalVariationDto>>> RestoreGlobalVariation(Guid id) =>
        Ok(await _mediator.SendCommand(new RestoreGlobalVariationCommand(id)));

    /// <summary>
    /// Archives the row when a product uses it, deletes it when none does — see
    /// <see cref="DeleteGlobalVariationCommandHandler"/>. The picker knows which it will be, because
    /// it renders the same count.
    /// </summary>
    [HttpDelete("{id}")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> DeleteGlobalVariation(Guid id) =>
        Ok(await _mediator.SendCommand(new DeleteGlobalVariationCommand(id)));
}
