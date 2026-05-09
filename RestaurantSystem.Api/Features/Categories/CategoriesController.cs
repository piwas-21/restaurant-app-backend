using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Commands.DeleteCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Commands.ReorderCategoriesCommand;
using RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryImageCommand;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Categories.Dtos.Requests;
using RestaurantSystem.Api.Features.Categories.Queries.GetCategoriesQuery;
using RestaurantSystem.Api.Features.Categories.Queries.GetCategoryByIdQuery;
using RestaurantSystem.Api.Features.Categories.Queries.GetCategoryProductsQuery;

namespace RestaurantSystem.Api.Features.Categories;

/// <summary>
/// Controller for managing product categories
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public CategoriesController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all categories with optional filters
    /// </summary>
    /// <param name="query">Query parameters for filtering and pagination</param>
    /// <returns>Paginated list of categories</returns>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<CategoryDto>>>> GetCategories(
        [FromQuery] GetCategoriesQuery query)
    {
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get category by ID
    /// </summary>
    /// <param name="id">Category ID</param>
    /// <returns>Category details with featured products</returns>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CategoryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CategoryDetailDto>>> GetCategory(Guid id)
    {
        var query = new GetCategoryByIdQuery(id);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get products in a specific category
    /// </summary>
    /// <param name="id">Category ID</param>
    /// <param name="pageNumber">Page number (default: 1)</param>
    /// <param name="pageSize">Page size (default: 10)</param>
    /// <param name="isActive">Filter by active status</param>
    /// <param name="isAvailable">Filter by availability</param>
    /// <returns>Paginated list of products in the category</returns>
    [HttpGet("{id}/products")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CategoryProductDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<CategoryProductDto>>>> GetCategoryProducts(
        Guid id,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] bool? isActive = null,
        [FromQuery] bool? isAvailable = null)
    {
        var query = new GetCategoryProductsQuery(id, pageNumber, pageSize, isActive, isAvailable);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Create a new category
    /// </summary>
    /// <param name="command">Category creation details</param>
    /// <returns>Created category</returns>
    [HttpPost]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> CreateCategory(
        [FromBody] CreateCategoryCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Update an existing category
    /// </summary>
    /// <param name="id">Category ID</param>
    /// <param name="command">Category update details</param>
    /// <returns>Updated category</returns>
    [HttpPut("{id}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> UpdateCategory(
        Guid id,
        [FromBody] UpdateCategoryCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest(ApiResponse<CategoryDto>.Failure("Category ID mismatch"));
        }

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Update category image
    /// </summary>
    /// <param name="id">Category ID</param>
    /// <param name="request">Image upload request</param>
    /// <returns>Updated category with new image</returns>
    [HttpPut("{id}/image")]
    [Consumes("multipart/form-data")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> UpdateCategoryImage(
        Guid id,
        [FromForm] UpdateCategoryImageRequest request)
    {
        var command = new UpdateCategoryImageCommand(id, request.Image);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete a category (soft delete)
    /// </summary>
    /// <param name="id">Category ID</param>
    /// <returns>Success message</returns>
    [HttpDelete("{id}")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<string>>> DeleteCategory(Guid id)
    {
        var command = new DeleteCategoryCommand(id);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Reorder categories display order
    /// </summary>
    /// <param name="command">List of category IDs with new display orders</param>
    /// <returns>Success message</returns>
    [HttpPut("reorder")]
    [RequireAdmin]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<string>>> ReorderCategories(
        [FromBody] ReorderCategoriesCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }
}
