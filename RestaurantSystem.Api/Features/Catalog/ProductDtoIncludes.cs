using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>
/// The navigations <see cref="ProductDtoMapper"/> reads, loaded in one place.
/// </summary>
/// <remarks>
/// Written as one extension rather than as a docblock asking each caller to remember the list —
/// the same call <c>OrderQueryIncludes</c> made, for the same reason. Three write paths reload a
/// product to echo it back, and the mapper projects every collection for any product, so a caller
/// that forgets one does not fail: it answers with an EMPTY collection, which reads as "this
/// product has no variations / no recipe / no sections". #468 added a fifth level to the chain and
/// made three near-copies of it, which is where this came from.
/// <para>
/// A bundle carries the product-specific collections empty and a plain dish carries no menu
/// definition, so one include set is correct for both — the nested reads are simply no-ops on the
/// side that has none.
/// </para>
/// </remarks>
public static class ProductDtoIncludes
{
    /// <summary>
    /// <c>AsSplitQuery</c> is part of the contract, not a caller's choice: this is six collection
    /// includes over one root, and as a single statement they multiply into each other's rows
    /// (S8733).
    /// </summary>
    public static IQueryable<Product> WithProductDtoNavigations(this IQueryable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        return products
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Descriptions)
            .Include(p => p.Variations)
                .ThenInclude(v => v.Descriptions)
            .Include(p => p.SuggestedSideItems)
                .ThenInclude(si => si.SideItemProduct)
            .Include(p => p.DetailedIngredients)
                .ThenInclude(di => di.Descriptions)
            // Down to the option products' own recipes: the shared bundle mapper projects them, and
            // an unloaded collection is EMPTY rather than absent, so the echo would state that every
            // option of every bundle has no ingredients.
            .Include(p => p.MenuDefinition!.Sections)
                .ThenInclude(s => s.Items)
                    .ThenInclude(i => i.Product.DetailedIngredients)
                        .ThenInclude(di => di.Descriptions)
            .AsSplitQuery();
    }
}
