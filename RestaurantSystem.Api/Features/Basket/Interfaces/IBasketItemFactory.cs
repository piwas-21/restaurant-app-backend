using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Interfaces;

/// <summary>
/// Builds <see cref="BasketItem"/> entities from an add-to-basket request.
/// Extracted from <c>BasketService.AddItemToBasketAsync</c> (Sprint 3 god-class
/// decomposition). The factory computes pricing and serialises the customisation
/// columns but does NOT persist — the caller adds the returned item(s) to the context.
/// </summary>
public interface IBasketItemFactory
{
    /// <summary>
    /// Builds a new non-menu basket item for the given product (and optional variation):
    /// unit price + ingredient customisation + side-item surcharges, with the selected
    /// side-items and ingredient quantities serialised to their JSON columns. Side-item
    /// prices are resolved from the database.
    /// </summary>
    Task<BasketItem> BuildRegularItemAsync(Product product, ProductVariation? variation, AddToBasketDto item, Guid basketId);

    /// <summary>
    /// Builds a menu (bundle) basket item: validates each section's required/min/max
    /// selection rules, computes the base + section-option + child-ingredient
    /// customisation price, and constructs the parent item with its child option items
    /// attached via <see cref="BasketItem.ChildBasketItems"/>. The caller adds the returned
    /// parent to the context — EF cascades the children, so nothing is persisted unless the
    /// whole graph builds successfully. <paramref name="product"/> must have its
    /// <c>MenuDefinition.Sections.Items.Product</c> graph eagerly loaded.
    /// </summary>
    Task<BasketItem> BuildMenuItemAsync(Product product, AddToBasketDto item, Guid basketId);
}
