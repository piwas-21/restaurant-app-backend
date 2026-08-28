using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.AttachGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.CreateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.DeleteGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.RestoreGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.UpdateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientByIdQuery;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientProductsQuery;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientsQuery;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.SearchGlobalIngredientsQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.GlobalIngredients;

[ApiController]
[Route("api/global-ingredients")]
public class GlobalIngredientsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public GlobalIngredientsController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<GlobalIngredientDto>>>> GetGlobalIngredients() =>
        Ok(await _mediator.SendQuery(new GetGlobalIngredientsQuery()));

    /// <summary>
    /// The archive drawer — admin only, because it exists to undo an admin action and because
    /// nothing on a guest surface has any use for a row that is off the shelf. The literal segment
    /// wins over the <c>{id}</c> route below, which is why it is declared here.
    /// </summary>
    [HttpGet("archived")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<List<GlobalIngredientDto>>>> GetArchivedGlobalIngredients() =>
        Ok(await _mediator.SendQuery(new GetGlobalIngredientsQuery(ArchivedOnly: true)));

    [HttpGet("{id}")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> GetGlobalIngredient(Guid id) =>
        Ok(await _mediator.SendQuery(new GetGlobalIngredientByIdQuery(id)));

    [HttpGet("search")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<GlobalIngredientDto>>>> SearchIngredients(
        [FromQuery] string query,
        [FromQuery] int limit = 10) =>
        Ok(await _mediator.SendQuery(new SearchGlobalIngredientsQuery(query, limit)));

    [HttpPost]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> CreateGlobalIngredient(
        [FromBody] CreateGlobalIngredientDto body) =>
        Ok(await _mediator.SendCommand(new CreateGlobalIngredientCommand(
            body.DefaultName, body.ImageUrl, body.Translations, body.Kind)));

    [HttpPut("{id}")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> UpdateGlobalIngredient(
        Guid id,
        [FromBody] UpdateGlobalIngredientDto body) =>
        Ok(await _mediator.SendCommand(new UpdateGlobalIngredientCommand(
            id, body.DefaultName, body.ImageUrl, body.IsActive, body.Translations, body.Kind)));

    /// <summary>
    /// WHICH products carry a copy of this row — the drill-down behind S3's "used on N items", and
    /// what a blast-radius confirm subtracts to say how many the next action would actually change
    /// (plan D6). Admin only: it is a catalogue-management view, and no guest surface has a use for
    /// it.
    /// </summary>
    [HttpGet("{id}/products")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<List<CatalogUsageProductDto>>>> GetProductsUsing(Guid id) =>
        Ok(await _mediator.SendQuery(new GetGlobalIngredientProductsQuery(id)));

    /// <summary>
    /// Copies this library row onto many products at once (plan S8). The caller sends the product
    /// ids it just showed the admin — there is no category target, so the confirm and the payload
    /// are the same list. See <see cref="AttachGlobalIngredientCommandHandler"/> for what is
    /// skipped, what refuses the whole batch, and why a required ingredient is not allowed here.
    /// </summary>
    [HttpPost("{id}/attach")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<AttachGlobalIngredientResultDto>>> AttachGlobalIngredient(
        Guid id,
        [FromBody] AttachGlobalIngredientDto body) =>
        Ok(await _mediator.SendCommand(new AttachGlobalIngredientCommand(id, body)));

    [HttpPost("{id}/restore")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> RestoreGlobalIngredient(Guid id) =>
        Ok(await _mediator.SendCommand(new RestoreGlobalIngredientCommand(id)));

    /// <summary>
    /// Archives the row when a product uses it, deletes it when none does — see
    /// <see cref="DeleteGlobalIngredientCommandHandler"/>. The picker knows which it will be,
    /// because it renders the same count.
    /// </summary>
    [HttpDelete("{id}")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> DeleteGlobalIngredient(Guid id) =>
        Ok(await _mediator.SendCommand(new DeleteGlobalIngredientCommand(id)));
}
