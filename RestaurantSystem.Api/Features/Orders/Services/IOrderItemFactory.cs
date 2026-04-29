using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Builds <see cref="OrderItem"/>(s) from a <see cref="CreateOrderItemDto"/>
/// and appends them to an order. Two top-level shapes are supported:
///
/// 1. Menu-bundle items (<c>itemDto.MenuId.HasValue</c>): produces a single
///    <see cref="OrderItem"/> using the menu's <c>BasePrice</c>. No
///    recursion — the bundle's selections are encoded in <c>ItemTotal</c>
///    and <c>IngredientQuantitiesJson</c>.
/// 2. Product items (<c>itemDto.ProductId.HasValue</c>): produces an
///    <see cref="OrderItem"/> with optional variation pricing, then
///    recurses on <c>itemDto.ChildItems</c> to attach nested customisation
///    items via <c>OrderItem.ParentOrderItem</c>.
///
/// Caller computes subtotal from the resulting <c>order.Items</c> via
/// <c>order.Items.Sum(i =&gt; i.ItemTotal)</c> after all top-level items
/// have been added.
///
/// Extracted from <c>CreateOrderCommandHandler</c> in Sprint 2 task 2.8.
/// </summary>
public interface IOrderItemFactory
{
    /// <summary>
    /// Adds the top-level item (and, for product paths, its recursive children)
    /// to <paramref name="order"/>.
    /// </summary>
    /// <returns>
    /// <c>null</c> on success. A user-facing error message if the referenced
    /// menu or top-level product is not found — the caller should surface
    /// that as <c>ApiResponse&lt;T&gt;.Failure(...)</c>. Nested missing
    /// products (deeper than the top level) throw
    /// <c>NotFoundException</c> instead, matching the original handler.
    /// </returns>
    Task<string?> AddItemAsync(Order order, CreateOrderItemDto itemDto, CancellationToken cancellationToken);
}
