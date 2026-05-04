using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;
using RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuBundleCommand;
using RestaurantSystem.Api.Features.Menus.Commands.DeleteMenuBundleCommand;
using RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundleByIdQuery;
using RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundlesQuery;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Features.Menus;

[ApiController]
[Route("api/[controller]")]
public class MenusController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public MenusController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Create a new menu bundle
    /// </summary>
    [HttpPost]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<ProductDto>>> CreateMenuBundle([FromBody] CreateMenuBundleCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Get all menu bundles with pagination
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<MenuBundleDto>>>> GetMenuBundles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeUnavailable = false)
    {
        // Only admins can view unavailable menus
        if (includeUnavailable && !User.IsInRole("Admin"))
        {
            return Unauthorized(ApiResponse<PagedResult<MenuBundleDto>>.Failure("Only admins can view unavailable menus"));
        }

        var query = new GetMenuBundlesQuery(page, pageSize, null, includeUnavailable);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get a specific menu bundle by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<MenuBundleDto>>> GetMenuBundleById(Guid id)
    {
        var query = new GetMenuBundleByIdQuery(id);
        var result = await _mediator.SendQuery(query);

        if (!result.Success)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Update an existing menu bundle
    /// </summary>
    [HttpPut("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdateMenuBundle(Guid id, [FromBody] UpdateMenuBundleCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest(ApiResponse<ProductDto>.Failure("ID mismatch"));
        }

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete a menu bundle
    /// </summary>
    [HttpDelete("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> DeleteMenuBundle(Guid id)
    {
        var command = new DeleteMenuBundleCommand(id);
        var result = await _mediator.SendCommand(command);

        if (!result.Success)
        {
            return NotFound(result);
        }

        return Ok(result);
    }
}
