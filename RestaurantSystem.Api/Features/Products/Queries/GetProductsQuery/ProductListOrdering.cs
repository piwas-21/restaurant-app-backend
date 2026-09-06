using System.Linq.Expressions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;

/// <summary>
/// How the public product list is SORTED — the two arms are chosen per caller in
/// <see cref="GetProductsQueryHandler"/> (guest, no CategoryId) versus everyone else.
/// </summary>
/// <remarks>
/// <para>
/// The guest's ALL view reads as one menu, so it follows the menu's own structure: the product's
/// PRIMARY category <c>DisplayOrder</c> first, the product's own <c>DisplayOrder</c> second
/// (partner feedback 2026-09-06 — a flat sort interleaved categories). A product listed in two
/// categories sorts at its primary one; an orphan (no live primary category) sorts LAST via the
/// coalesce, never first. Category TABS are unaffected by the term: every product shown shares
/// the category, so it is constant there.
/// </para>
/// <para>
/// Every arm ends in the SAME <c>ThenBy(Name).ThenBy(Id)</c> tail the handler appends — that tail
/// is REQUIRED by the <c>AsSplitQuery</c> below it: Skip/Take correlates the split round-trips by
/// the ordering, so a non-unique one can attach one product's images to another product.
/// </para>
/// </remarks>
internal static class ProductListOrdering
{
    /// <summary>The orphan's slot: int.MaxValue, so a category-less product can never sort first.</summary>
    public const int OrphanCategoryOrder = int.MaxValue;

    public static IOrderedQueryable<Product> ForGuestAllView(IQueryable<Product> products) =>
        products.OrderBy(PrimaryCategoryDisplayOrder).ThenBy(p => p.DisplayOrder);

    public static IOrderedQueryable<Product> Flat(IQueryable<Product> products) =>
        products.OrderBy(p => p.DisplayOrder);

    /// <summary>EF-translatable: MIN over the primary category links, NULL (none) coalesced last.</summary>
    private static Expression<Func<Product, int>> PrimaryCategoryDisplayOrder =>
        p => p.ProductCategories
            .Where(pc => pc.IsPrimary)
            .Select(pc => (int?)pc.Category.DisplayOrder)
            .Min() ?? OrphanCategoryOrder;
}
