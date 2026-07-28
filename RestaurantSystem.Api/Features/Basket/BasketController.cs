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

/// <summary>
/// The basket and its LINES. The channel lives on <see cref="BasketChannelController"/> — same
/// route prefix, different concern (ORDER-TYPE-AVAILABILITY-PLAN §9.7).
/// </summary>
public class BasketController : BasketControllerBase
{
    public BasketController(CustomMediator mediator) : base(mediator)
    {
    }

    /// <summary>
    /// Get the current basket
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> GetBasket([FromHeader(Name = SessionIdHeader)] string sessionId)
    {
        if (MissingSession<BasketDto>(sessionId) is { } error) return error;

        return Ok(await Mediator.SendQuery(new GetBasketQuery(sessionId)));
    }

    /// <summary>
    /// Get basket summary (item count and total)
    /// </summary>
    [HttpGet("summary")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketSummaryDto>>> GetBasketSummary([FromHeader(Name = SessionIdHeader)] string sessionId)
    {
        if (MissingSession<BasketSummaryDto>(sessionId) is { } error) return error;

        return Ok(await Mediator.SendQuery(new GetBasketSummaryQuery(sessionId)));
    }

    /// <summary>
    /// Add item to basket
    /// </summary>
    [HttpPost("items")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> AddToBasket(
        [FromHeader(Name = SessionIdHeader)] string sessionId,
        [FromBody] AddToBasketDto request)
    {
        if (MissingSession<BasketDto>(sessionId) is { } error) return error;

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

        return Ok(await Mediator.SendCommand(command));
    }

    /// <summary>
    /// Update basket item quantity or special instructions
    /// </summary>
    [HttpPut("items/{basketItemId}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> UpdateBasketItem(
        [FromHeader(Name = SessionIdHeader)] string sessionId,
        Guid basketItemId,
        [FromBody] UpdateBasketItemDto request)
    {
        if (MissingSession<BasketDto>(sessionId) is { } error) return error;

        var command = new UpdateBasketItemCommand(
            sessionId,
            basketItemId,
            request.Quantity,
            request.SpecialInstructions);

        return Ok(await Mediator.SendCommand(command));
    }

    /// <summary>
    /// Remove item from basket
    /// </summary>
    [HttpDelete("items/{basketItemId}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> RemoveFromBasket(
        [FromHeader(Name = SessionIdHeader)] string sessionId,
        Guid basketItemId)
    {
        if (MissingSession<BasketDto>(sessionId) is { } error) return error;

        return Ok(await Mediator.SendCommand(new RemoveFromBasketCommand(sessionId, basketItemId)));
    }

    /// <summary>
    /// Clear all items from basket
    /// </summary>
    [HttpDelete]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketDto>>> ClearBasket([FromHeader(Name = SessionIdHeader)] string sessionId)
    {
        if (MissingSession<BasketDto>(sessionId) is { } error) return error;

        return Ok(await Mediator.SendCommand(new ClearBasketCommand(sessionId)));
    }

    /// <summary>
    /// Apply promo code to basket. Not implemented — see <see cref="RemovePromoCode"/>.
    /// </summary>
    [HttpPost("promo-code")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<BasketDto>> ApplyPromoCode(
        [FromHeader(Name = SessionIdHeader)] string sessionId,
        [FromBody] ApplyPromoCodeRequest request)
        => BadRequest(ApiResponse<BasketDto>.Failure("Promo code functionality not yet implemented"));

    /// <summary>
    /// Remove promo code from basket.
    /// </summary>
    /// <remarks>
    /// Both promo endpoints are deliberate STUBS, not dead code: <c>basketService.ts</c> calls them
    /// and the cart renders the 400's message, so deleting them would turn a stated "not implemented
    /// yet" into a 404 the client has no wording for. Synchronous now — they were <c>async</c> with
    /// no <c>await</c>, which needed a <c>#pragma warning disable CS1998</c> pair each to compile.
    /// </remarks>
    [HttpDelete("promo-code")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<BasketDto>> RemovePromoCode([FromHeader(Name = SessionIdHeader)] string sessionId)
        => BadRequest(ApiResponse<BasketDto>.Failure("Promo code functionality not yet implemented"));
}
