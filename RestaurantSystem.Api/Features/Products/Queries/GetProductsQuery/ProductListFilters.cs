using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;

/// <summary>
/// The row-level filters shared by every <see cref="GetProductsQuery"/> caller, applied in one
/// place so guest/staff defaults cannot drift between surfaces. Ordering lives beside it in
/// <see cref="ProductListOrdering"/>.
/// </summary>
public static class ProductListFilters
{
    public static async Task<IQueryable<Product>> Apply(
        ApplicationDbContext context,
        GetProductsQuery query,
        ICurrentUserService currentUser,
        IQueryable<Product> productsQuery,
        CancellationToken cancellationToken)
    {
        if (query.CategoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == query.CategoryId.Value));
        }
        else if (!currentUser.IsStaff)
        {
            // The guest ALL view honours the category "hide from the All tab" flag (2026-09-06).
            // Rationale + the soft-delete conflation it avoids: GuestAllViewVisibility.
            productsQuery = await GuestAllViewVisibility.ExcludeHiddenAsync(context, productsQuery, cancellationToken);
        }

        if (query.Type.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Type == query.Type.Value);
        }
        else if (!query.IncludeMenus)
        {
            // Default behavior: Exclude Menu bundles unless specifically requested via Type
            // or opted into via IncludeMenus.
            productsQuery = productsQuery.Where(p => p.Type != ProductType.Menu);
        }

        if (query.ExcludeType.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Type != query.ExcludeType.Value);
        }

        if (!query.IncludeComponents)
        {
            // Components are not catalogue items. Excluded by default so no guest surface has to
            // remember to ask; the admin list and the bundle option picker opt in.
            productsQuery = productsQuery.Where(p => !p.IsComponent);
        }

        // #438. Hiding a deactivated product used to be OPT-IN, per caller: the filter ran only when
        // the caller asked for it. Three of five callers remembered; the web guest menu was
        // ACCIDENTALLY safe (a client-side `isVisible` filter drops them after they are sent) and
        // the mobile category browse showed them. An owner switching a dish off did not remove it
        // from that menu.
        //
        // So the default is now the CALLER's, not the query's: a guest never sees a deactivated
        // product, and back-of-house keeps today's unfiltered default because seeing an inactive
        // item IS the point of the admin list's Active toggle. `IsStaff` is the shared dividing
        // line — the same one order ownership turns on — not a fresh predicate.
        //
        // For a guest the filter is FORCED, not merely defaulted: `?isActive=false` from an
        // unauthenticated caller must not become a way to enumerate what the owner switched off.
        if (!currentUser.IsStaff)
        {
            productsQuery = productsQuery.Where(p => p.IsActive);
        }
        else if (query.IsActive.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsActive == query.IsActive.Value);
        }

        if (query.IsAvailable.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsAvailable == query.IsAvailable.Value);
        }

        if (query.isSpeacial.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsSpecial == query.isSpeacial.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchLower = query.Search.ToLower();

            productsQuery = productsQuery.Where(p => p.Name.ToLower().Contains(searchLower) || p.Descriptions.Any(c => c.Name.ToLower().Contains(searchLower)));
        }

        return productsQuery;
    }
}
