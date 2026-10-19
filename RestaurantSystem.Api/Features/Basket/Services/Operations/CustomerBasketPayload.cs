using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

// Execution and cancellation must hash exactly the same normalized request, including nulls.
internal static class CustomerBasketPayload
{
    public static string Add(AddToBasketDto request) =>
        CustomerBasketOperationHash.Payload(request with { OperationId = null });

    public static string Mutation(Guid basketItemId, CustomerBasketItemMutationDto request) =>
        CustomerBasketOperationHash.Payload(new { Kind = "root-item-mutation-v1", BasketItemId = basketItemId, Request = request });
    public static string Channel(CustomerBasketChannelDto request) =>
        CustomerBasketOperationHash.Payload(new { Kind = "basket-channel-v1", Request = request });
}
