using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public sealed class CustomerBasketChannelService(
    ICustomerBasketOperationService operations, IBasketService baskets, IBasketChannelService channels,
    IOrderTypeConfigurationService configuration, ICurrentUserService currentUser) : ICustomerBasketChannelService
{
    public Task<BasketDto> SwitchAsync(string sessionId, CustomerBasketChannelDto request,
        CancellationToken cancellationToken) =>
        operations.ExecuteAsync(sessionId, request.OperationId, CustomerBasketPayload.Channel(request),
            () => ApplyAsync(sessionId, request, cancellationToken), cancellationToken);

    private async Task<BasketDto> ApplyAsync(string sessionId, CustomerBasketChannelDto request,
        CancellationToken cancellationToken)
    {
        var current = await baskets.GetBasketAsync(sessionId, currentUser.UserId);
        if (!CustomerBasketRevision.Matches(current, request.ExpectedBasketId,
            request.ExpectedPurchaseFingerprint, allowMissing: true))
            throw new ConflictException("Your basket changed. Review it before switching order type.");
        await CustomerBasketAvailability.EnsureChannelAsync(configuration, request.OrderType, cancellationToken);
        var switched = await channels.SetReviewedOrderTypeAsync(sessionId, currentUser.UserId, request.OrderType,
            request.ConfirmedConflictItemIds.ToHashSet(), cancellationToken);
        return switched.Applied && switched.Basket is not null ? switched.Basket
            : throw new ConflictException("Channel conflicts changed. Review the affected items again.");
    }

}
