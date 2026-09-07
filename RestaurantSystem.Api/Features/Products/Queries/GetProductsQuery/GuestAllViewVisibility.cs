using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;

/// <summary>
/// The guest ALL view honours the category's "hide from the All tab" flag (partner request
/// 2026-09-06). A product is excluded only when EVERY one of its category links points at a
/// LIVE hidden category — so:
///   * a dish shared with a visible category stays on the menu (mixed case);
///   * a product whose categories are all SOFT-DELETED keeps the pinned behaviour of staying
///     listed (<see cref="SoftDeletedCategoryAvailabilityTests"/>): a deletion is not an owner's
///     "hide" decision, and deleted ids are deliberately not in the set;
///   * a product with no links at all is untouched, exactly as before.
/// Staff callers skip this entirely — the admin must see everything to manage the flag.
/// </summary>
/// <remarks>
/// Expressed as link-id NOT-IN over the hidden set, never through the <c>Category</c>
/// navigation: the nav's join + global-filter + null semantics under a soft-deleted principal
/// are exactly what the first draft got wrong, and the full suite caught it.
/// With no hidden categories the query is returned unchanged — zero behaviour change for the
/// tenants that never use the flag.
/// </remarks>
internal static class GuestAllViewVisibility
{
    public static async Task<IQueryable<Product>> ExcludeHiddenAsync(
        ApplicationDbContext context,
        IQueryable<Product> productsQuery,
        CancellationToken cancellationToken)
    {
        var hiddenCategoryIds = await context.Categories
            .Where(c => !c.IsDeleted && c.IsHiddenFromAllTab)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (hiddenCategoryIds.Count == 0)
        {
            return productsQuery;
        }

        return productsQuery.Where(p =>
            p.ProductCategories.Any(pc => !hiddenCategoryIds.Contains(pc.CategoryId))
            || !p.ProductCategories.Any());
    }
}
