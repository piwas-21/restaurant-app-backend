using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.Basket.Commands.AddToBasketCommand;
using RestaurantSystem.Api.Features.Basket.Commands.ClearBasketCommand;
using RestaurantSystem.Api.Features.Basket.Commands.RemoveFromBasketCommand;
using RestaurantSystem.Api.Features.Basket.Commands.UpdateBasketItemCommand;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Queries.GetBasketQuery;
using RestaurantSystem.Api.Features.Basket.Queries.GetBasketSummaryQuery;

namespace RestaurantSystem.Api.Features.Basket;

[ApiController]
[Route("api/[controller]")]
public class BasketController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly ILogger<BasketController> _logger;

    public BasketController(CustomMediator mediator, ILogger<BasketController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Get the current basket
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> GetBasket([FromHeader(Name = "X-Session-Id")] string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return BadRequest(ApiResponse<BasketDto>.Failure("Session ID is required"));
        }

        var query = new GetBasketQuery(sessionId);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get basket summary (item count and total)
    /// </summary>
    [HttpGet("summary")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketSummaryDto>>> GetBasketSummary([FromHeader(Name = "X-Session-Id")] string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return BadRequest(ApiResponse<BasketSummaryDto>.Failure("Session ID is required"));
        }

        var query = new GetBasketSummaryQuery(sessionId);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Add item to basket
    /// </summary>
    [HttpPost("items")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> AddToBasket(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] AddToBasketDto request)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return BadRequest(ApiResponse<BasketDto>.Failure("Session ID is required"));
        }

        var command = new AddToBasketCommand(
            sessionId,
            request.ProductId,
            request.ProductVariationId,
            request.MenuId,
            request.Quantity,
            request.SpecialInstructions,
            request.SelectedIngredients,
            request.ExcludedIngredients,
            request.AddedIngredients,
            request.IngredientQuantities,
            request.SelectedSideItems,
            request.SelectedMenuOptions);

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Update basket item quantity or special instructions
    /// </summary>
    [HttpPut("items/{basketItemId}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> UpdateBasketItem(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        Guid basketItemId,
        [FromBody] UpdateBasketItemDto request)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return BadRequest(ApiResponse<BasketDto>.Failure("Session ID is required"));
        }

        var command = new UpdateBasketItemCommand(
            sessionId,
            basketItemId,
            request.Quantity,
            request.SpecialInstructions);

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Remove item from basket
    /// </summary>
    [HttpDelete("items/{basketItemId}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> RemoveFromBasket(
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        Guid basketItemId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return BadRequest(ApiResponse<BasketDto>.Failure("Session ID is required"));
        }

        var command = new RemoveFromBasketCommand(sessionId, basketItemId);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Clear all items from basket
    /// </summary>
    [HttpDelete]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> ClearBasket([FromHeader(Name = "X-Session-Id")] string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return BadRequest(ApiResponse<BasketDto>.Failure("Session ID is required"));
        }

        var command = new ClearBasketCommand(sessionId);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Apply promo code to basket
    /// </summary>
    [HttpPost("promo-code")]
    [AllowAnonymous]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<ActionResult<ApiResponse<BasketDto>>> ApplyPromoCode(
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        [FromHeader(Name = "X-Session-Id")] string sessionId,
        [FromBody] ApplyPromoCodeRequest request)
    {
        // TODO: Implement when promo code functionality is ready
        return BadRequest(ApiResponse<BasketDto>.Failure("Promo code functionality not yet implemented"));
    }

    /// <summary>
    /// Remove promo code from basket
    /// </summary>
    [HttpDelete("promo-code")]
    [AllowAnonymous]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<ActionResult<ApiResponse<BasketDto>>> RemovePromoCode([FromHeader(Name = "X-Session-Id")] string sessionId)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        // TODO: Implement when promo code functionality is ready
        return BadRequest(ApiResponse<BasketDto>.Failure("Promo code functionality not yet implemented"));
    }
}
