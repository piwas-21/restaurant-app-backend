using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public sealed class CustomerBasketItemMutationService(
    ICustomerBasketOperationService operations, IBasketService baskets, ICurrentUserService currentUser)
    : ICustomerBasketItemMutationService
{
    public Task<BasketDto> MutateAsync(string sessionId, Guid basketItemId, CustomerBasketItemMutationDto request,
        CancellationToken cancellationToken) =>
        operations.ExecuteAsync(sessionId, request.OperationId,
            CustomerBasketPayload.Mutation(basketItemId, request),
            () => ApplyAsync(sessionId, basketItemId, request), cancellationToken);

    private async Task<BasketDto> ApplyAsync(string sessionId, Guid basketItemId, CustomerBasketItemMutationDto request)
    {
        var current = await baskets.GetBasketAsync(sessionId, currentUser.UserId)
            ?? throw new NotFoundException("Basket unavailable.");
        if (current.Id != request.ExpectedBasketId
            || !BasketPurchaseFingerprint.Matches(request.ExpectedPurchaseFingerprint, current.PurchaseFingerprint))
            throw new ConflictException("Your basket changed. Refresh it before editing this item.");
        if (!current.Items.Any(item => item.Id == basketItemId))
            throw new NotFoundException("Basket item unavailable.");
        return request.Action switch
        {
            CustomerBasketItemAction.Update => await baskets.UpdateBasketItemAsync(sessionId, basketItemId,
                new UpdateBasketItemDto { Quantity = request.Quantity ?? throw new BadRequestException("Quantity is required."), SpecialInstructions = request.SpecialInstructions }),
            CustomerBasketItemAction.Remove => await baskets.RemoveItemFromBasketAsync(sessionId, basketItemId),
            _ => throw new BadRequestException("Unsupported basket operation.")
        };
    }
}
