using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos;

public sealed record CustomerBasketChannelPreviewDto(Guid BasketId, string PurchaseFingerprint,
    OrderType OrderType, IReadOnlyList<BasketChannelConflictDto> Conflicts);
