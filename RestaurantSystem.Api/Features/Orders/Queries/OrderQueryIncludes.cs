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
    /// Includes the line's frozen ingredient snapshot, plus everything a line with NO snapshot needs
    /// for its <c>KitchenType</c> and ingredient customizations, on both resolution paths: a
    /// product-backed line (<c>ProductId</c>) and a menu-backed one (<c>MenuId</c> with no
    /// <c>ProductId</c>, e.g. the legacy "Chief's Special").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Callers should pair this with <c>AsSplitQuery()</c>: these are sibling collections that
    /// cartesian-multiply in EF's default single-query mode.
    /// </para>
    /// <para>
    /// <b>What S1 removed, and what it deliberately did NOT.</b> The chain used to end
    /// <c>.ThenInclude(pi =&gt; pi.GlobalIngredient)</c> on both branches — a fourth and a sixth
    /// level loaded on every printer poll to supply a display name. S0n stopped reading that name
    /// (a global rename must not reword a placed order) and S1 froze the name on the order line
    /// instead, so those two levels now feed nothing at all and are gone.
    /// </para>
    /// <para>
    /// The <c>DetailedIngredients</c> levels STAY. S1 backfills nothing by design, so every order
    /// placed before it carries an id map and no snapshot, and resolves against the live recipe
    /// exactly as it always did. Dropping the catalog levels would not throw — it would silently
    /// blank the ingredient detail on all of history, which is the #234 failure mode again.
    /// </para>
    /// </remarks>
    public static IIncludableQueryable<Order, ICollection<ProductIngredient>> IncludeOrderLineGraph(
        this IQueryable<Order> query) =>
        query
            .Include(o => o.Items)
                .ThenInclude(i => i.IngredientSnapshots)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
            .Include(o => o.Items)
                .ThenInclude(i => i.Menu)
                    .ThenInclude(m => m!.MenuItems)
                        .ThenInclude(mi => mi.Product)
                            .ThenInclude(p => p.DetailedIngredients);
}
