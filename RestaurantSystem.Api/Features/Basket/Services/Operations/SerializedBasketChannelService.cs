using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public sealed class SerializedBasketChannelService(BasketChannelService inner, IBasketWriteCoordinator writes)
    : IBasketChannelService
{
    public Task<BasketChannelSwitchDto> SetOrderTypeAsync(string sessionId, Guid? userId,
        OrderType orderType, bool removeConflicts, CancellationToken cancellationToken = default) =>
        writes.RunAsync(sessionId, userId,
            () => inner.SetOrderTypeAsync(sessionId, userId, orderType, removeConflicts, cancellationToken), cancellationToken);

    public Task<BasketChannelSwitchDto> SetReviewedOrderTypeAsync(string sessionId, Guid? userId,
        OrderType orderType, IReadOnlySet<Guid> reviewedConflicts, CancellationToken cancellationToken = default) =>
        writes.RunAsync(sessionId, userId,
            () => inner.SetReviewedOrderTypeAsync(sessionId, userId, orderType, reviewedConflicts, cancellationToken), cancellationToken);

    public Task<BasketDto?> ClearOrderTypeAsync(string sessionId, Guid? userId,
        CancellationToken cancellationToken = default) =>
        writes.RunAsync(sessionId, userId,
            () => inner.ClearOrderTypeAsync(sessionId, userId, cancellationToken), cancellationToken);
}
