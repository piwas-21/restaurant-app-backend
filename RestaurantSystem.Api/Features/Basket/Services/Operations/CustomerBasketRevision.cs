using RestaurantSystem.Api.Features.Basket.Dtos;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

internal static class CustomerBasketRevision
{
    public static bool Matches(BasketDto? current, Guid expectedId, string? expectedFingerprint,
        bool allowMissing = false) => current is null
            ? allowMissing && expectedId == Guid.Empty && expectedFingerprint == string.Empty
            : current.Id == expectedId && BasketPurchaseFingerprint.Matches(expectedFingerprint, current.PurchaseFingerprint);
}
