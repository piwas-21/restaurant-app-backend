using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Queries;

/// <summary>
/// The entity graph <see cref="Services.OrderMappingService"/> needs to map an order line, shared
/// by every query that maps synchronously (the printer feed, the order list, focus orders).
/// <para>
/// It lives here because the mapper reads all of these through null-conditionals: a missing
/// include yields a silently null/empty DTO field rather than an exception, so three hand-copied
/// include chains drifting apart is exactly how issue #234 happened. Adding a navigation the
/// mapper reads means adding it here, once.
/// </para>
/// </summary>
public static class OrderQueryIncludes
{
    /// <summary>
    /// Includes everything an order line needs for its <c>KitchenType</c> and ingredient
    /// customizations, on both resolution paths: a product-backed line (<c>ProductId</c>) and a
    /// menu-backed one (<c>MenuId</c> with no <c>ProductId</c>, e.g. the legacy "Chief's Special").
    /// </summary>
    /// <remarks>
    /// Callers should pair this with <c>AsSplitQuery()</c>: these are sibling collections that
    /// cartesian-multiply in EF's default single-query mode.
    /// </remarks>
    public static IIncludableQueryable<Order, GlobalIngredient?> IncludeOrderLineGraph(
        this IQueryable<Order> query) =>
        query
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
                        .ThenInclude(pi => pi.GlobalIngredient)
            .Include(o => o.Items)
                .ThenInclude(i => i.Menu)
                    .ThenInclude(m => m!.MenuItems)
                        .ThenInclude(mi => mi.Product)
                            .ThenInclude(p => p.DetailedIngredients)
                                .ThenInclude(pi => pi.GlobalIngredient);
}
