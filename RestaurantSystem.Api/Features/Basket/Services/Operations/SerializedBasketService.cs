using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

/// <summary>Wraps existing basket business rules; reads stay free of write locks.</summary>
public sealed class SerializedBasketService(BasketService inner, IBasketWriteCoordinator writes,
    ICurrentUserService currentUser) : IBasketService
{
    public Task<BasketDto?> GetBasketAsync(string sessionId, Guid? userId = null) =>
        inner.GetBasketAsync(sessionId, userId);
    public Task<BasketSummaryDto?> GetBasketSummaryAsync(string sessionId, Guid? userId = null) =>
        inner.GetBasketSummaryAsync(sessionId, userId);
    public Task<BasketDto> AddItemToBasketAsync(string sessionId, Guid? userId, AddToBasketDto item) =>
        writes.RunAsync(sessionId, userId, () => inner.AddItemToBasketAsync(sessionId, userId, item));
    public Task<BasketDto> UpdateBasketItemAsync(string sessionId, Guid basketItemId, UpdateBasketItemDto update) =>
        writes.RunAsync(sessionId, currentUser.UserId, () => inner.UpdateBasketItemAsync(sessionId, basketItemId, update));
    public Task<BasketDto> RemoveItemFromBasketAsync(string sessionId, Guid basketItemId) =>
        writes.RunAsync(sessionId, currentUser.UserId, () => inner.RemoveItemFromBasketAsync(sessionId, basketItemId));
    public Task<BasketDto> ClearBasketAsync(string sessionId) =>
        writes.RunAsync(sessionId, currentUser.UserId, () => inner.ClearBasketAsync(sessionId));
    public Task<BasketDto> ApplyPromoCodeAsync(string sessionId, string promoCode) =>
        writes.RunAsync(sessionId, currentUser.UserId, () => inner.ApplyPromoCodeAsync(sessionId, promoCode));
    public Task<BasketDto> RemovePromoCodeAsync(string sessionId) =>
        writes.RunAsync(sessionId, currentUser.UserId, () => inner.RemovePromoCodeAsync(sessionId));
    public Task<BasketDto> MergeAnonymousBasketAsync(string sessionId, Guid userId) =>
        writes.MergeAsync(sessionId, userId, () => inner.MergeAnonymousBasketAsync(sessionId, userId));
    public Task RecalculateBasketTotalsAsync(Guid basketId) =>
        writes.RecalculateAsync(basketId, () => inner.RecalculateBasketTotalsAsync(basketId));
}
