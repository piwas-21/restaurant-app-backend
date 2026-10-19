using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public sealed class CustomerBasketChannelPreviewService(IBasketRepository repository,
    IBasketMappingService mapping, IBasketChannelConflictService conflicts, ICurrentUserService currentUser,
    IHttpContextAccessor httpContext, CustomMediator mediator, IOrderTypeConfigurationService configuration)
    : ICustomerBasketChannelPreviewService
{
    public async Task<CustomerBasketChannelPreviewDto> PreviewAsync(string sessionId, OrderType orderType,
        CancellationToken cancellationToken)
    {
        CustomerBasketAccess.EnsureCustomerOrGuest(currentUser, httpContext.HttpContext);
        CustomerBasketOperationHash.Session(sessionId);
        await CustomerBasketAvailability.EnsureOrderingAsync(mediator, cancellationToken);
        await CustomerBasketAvailability.EnsureChannelAsync(configuration, orderType, cancellationToken);
        var basket = await repository.FindBasketAsync(sessionId, currentUser.UserId);
        if (basket is null) return new(Guid.Empty, string.Empty, orderType, []);
        var view = await mapping.MapAsync(basket);
        return new(view.Id, view.PurchaseFingerprint, orderType,
            await conflicts.FindAsync(basket, orderType, cancellationToken));
    }

}
