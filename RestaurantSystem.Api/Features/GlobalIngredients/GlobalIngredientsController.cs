using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.CreateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.DeleteGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.UpdateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientByIdQuery;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientsQuery;
using RestaurantSystem.Api.Features.GlobalIngredients.Queries.SearchGlobalIngredientsQuery;

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
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<GlobalIngredientDto>>>> GetGlobalIngredients() =>
        Ok(await _mediator.SendQuery(new GetGlobalIngredientsQuery()));

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> GetGlobalIngredient(Guid id) =>
        Ok(await _mediator.SendQuery(new GetGlobalIngredientByIdQuery(id)));

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<GlobalIngredientDto>>>> SearchIngredients(
        [FromQuery] string query,
        [FromQuery] int limit = 10) =>
        Ok(await _mediator.SendQuery(new SearchGlobalIngredientsQuery(query, limit)));

    [HttpPost]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> CreateGlobalIngredient(
        [FromBody] CreateGlobalIngredientDto body) =>
        Ok(await _mediator.SendCommand(new CreateGlobalIngredientCommand(
            body.DefaultName, body.ImageUrl, body.Translations)));

    [HttpPut("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<GlobalIngredientDto>>> UpdateGlobalIngredient(
        Guid id,
        [FromBody] UpdateGlobalIngredientDto body) =>
        Ok(await _mediator.SendCommand(new UpdateGlobalIngredientCommand(
            id, body.DefaultName, body.ImageUrl, body.IsActive, body.Translations)));

    [HttpDelete("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> DeleteGlobalIngredient(Guid id) =>
        Ok(await _mediator.SendCommand(new DeleteGlobalIngredientCommand(id)));
}
