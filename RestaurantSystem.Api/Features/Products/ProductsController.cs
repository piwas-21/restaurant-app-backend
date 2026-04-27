using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Commands.DeleteProductCommand;
using RestaurantSystem.Api.Features.Products.Commands.DeleteProductImageCommand;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductImageCommand;
using RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;
using RestaurantSystem.Api.Features.Products.Commands.UploadProductImageCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Queries.GetProductByIdQuery;
using RestaurantSystem.Api.Features.Products.Queries.GetProductImagesQuery;
using RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;
using RestaurantSystem.Api.Features.Products.Queries.GetSpecialProductsQuery;
using RestaurantSystem.Api.Features.Products.Queries.GetFeaturedSpecialQuery;
using RestaurantSystem.Api.Features.Products.Commands.SetFeaturedSpecialCommand;
using RestaurantSystem.Api.Features.Products.Commands.UnsetFeaturedSpecialCommand;

namespace RestaurantSystem.Api.Features.Products;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public ProductsController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all products with optional filters
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductSummaryDto>>>> GetProducts(
        [FromQuery] GetProductsQuery query)
    {
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get all special products (IsSpecial = true)
    /// IMPORTANT: This must come before GET {id} to avoid route conflicts
    /// </summary>
    [HttpGet("specials")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<PagedResult<SpecialProductDto>>>> GetSpecialProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetSpecialProductsQuery(page, pageSize);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get the currently featured special (public endpoint)
    /// IMPORTANT: This must come before GET {id} to avoid route conflicts
    /// </summary>
    [HttpGet("featured-special")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<FeaturedSpecialDto?>>> GetFeaturedSpecial()
    {
        var query = new GetFeaturedSpecialQuery();
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get product by ID
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetProduct(Guid id)
    {
        var query = new GetProductByIdQuery(id);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Create a new product
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProductDto>>> CreateProduct([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Update a product
    /// </summary>
    [HttpPut("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdateProduct(
        Guid id,
        [FromBody] UpdateProductCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest(ApiResponse<ProductDto>.Failure("Product ID mismatch"));
        }

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    ///// <summary>
    ///// Delete a product
    ///// </summary>
    [HttpDelete("{id}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> DeleteProduct(Guid id)
    {
        var command = new DeleteProductCommand(id);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    // Product Image Endpoints

    /// <summary>
    /// Get all images for a product
    /// </summary>
    [HttpGet("{id}/images")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<ProductImageDto>>>> GetProductImages(Guid id)
    {
        var query = new GetProductImagesQuery(id);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Upload an image for a product
    /// </summary>
    [HttpPost("{id}/images")]
    [Consumes("multipart/form-data")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<ProductImageDto>>> UploadProductImage(
        Guid id,
        [FromForm] UploadProductImageDto request)
    {
        var command = new UploadProductImageCommand
        {
            ProductId = id,
            Image = request.Image,
            AltText = request.AltText,
            IsPrimary = request.IsPrimary,
            SortOrder = request.SortOrder
        };

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Upload multiple images for a product
    /// </summary>
    [HttpPost("{id}/images/bulk")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<List<ProductImageDto>>>> UploadMultipleProductImages(
        Guid id,
        [FromForm] UploadMultipleProductImagesDto request)
    {
        var command = new UploadMultipleProductImagesCommand(id, request.Images);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Update a product image
    /// </summary>
    [HttpPut("{productId}/images/{imageId}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<ProductImageDto>>> UpdateProductImage(
        Guid productId,
        Guid imageId,
        [FromBody] UpdateProductImageRequest request)
    {
        var command = new UpdateProductImageCommand(
            productId,
            imageId,
            request.AltText,
            request.IsPrimary,
            request.SortOrder
        );

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete a product image
    /// </summary>
    [HttpDelete("{productId}/images/{imageId}")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> DeleteProductImage(Guid productId, Guid imageId)
    {
        var command = new DeleteProductImageCommand(productId, imageId);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    // Special Products Endpoints - Note: specific routes moved before {id} route to avoid conflicts

    /// <summary>
    /// Set a product as the featured special (admin only)
    /// Only one product can be featured at a time
    /// </summary>
    [HttpPost("{id}/set-featured")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> SetFeaturedSpecial(Guid id)
    {
        var command = new SetFeaturedSpecialCommand(id);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Remove the currently featured special (admin only)
    /// </summary>
    [HttpDelete("featured-special")]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<string>>> UnsetFeaturedSpecial()
    {
        var command = new UnsetFeaturedSpecialCommand();
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }
}
