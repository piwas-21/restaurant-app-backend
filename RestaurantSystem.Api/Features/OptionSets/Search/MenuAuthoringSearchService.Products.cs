using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchProductsAsync(
        string normalizedQuery,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var pattern = LikePattern(normalizedQuery);
        var decisionTypes = new[]
        {
            MenuAuthoringCandidateTypes.Product,
            MenuAuthoringCandidateTypes.Component,
            MenuAuthoringCandidateTypes.Bundle
        };
        var decisions = _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery
                && decisionTypes.Contains(decision.CandidateType));
        var acceptedIds = decisions.Where(decision => decision.IsAccepted).Select(decision => decision.CandidateId);
        var rejectedIds = decisions.Where(decision => !decision.IsAccepted).Select(decision => decision.CandidateId);
        var products = _context.Products.AsNoTracking().Where(product => product.IsActive && product.IsAvailable);
        if (forKind == OptionSetKind.SuggestedSide)
        {
            products = products.Where(product => !product.IsComponent);
        }

        products = products.Where(product =>
            EF.Functions.ILike(product.Name, pattern, "\\") || acceptedIds.Contains(product.Id));
        products = products.Where(product => !rejectedIds.Contains(product.Id));

        var ranked = products.Select(product => new RankedProduct
        {
            Product = product,
            Rank = product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.BundleRank
                : product.IsComponent ? MenuAuthoringCandidateTypes.ComponentRank : MenuAuthoringCandidateTypes.ProductRank
        });
        if (cursor is not null) ranked = AfterCursor(ranked, cursor);
        return await ProjectProducts(ranked.OrderBy(row => EF.Functions.Collate(row.Product.Name, "C"))
            .ThenBy(row => row.Rank).ThenBy(row => row.Product.Id).Take(pageSize + 1))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RankedProduct> AfterCursor(
        IQueryable<RankedProduct> products, MenuAuthoringSearchCursor cursor) =>
        products.Where(row => EF.Functions.Collate(row.Product.Name, "C").CompareTo(cursor.Name) > 0
            || EF.Functions.Collate(row.Product.Name, "C").CompareTo(cursor.Name) == 0
            && (row.Rank > cursor.TypeRank || row.Rank == cursor.TypeRank
                && row.Product.Id.CompareTo(cursor.Id) > 0));

    private static IQueryable<MenuAuthoringSearchCandidateDto> ProjectProducts(
        IQueryable<RankedProduct> products) => products.Select(row => new MenuAuthoringSearchCandidateDto
        {
            Id = row.Product.Id,
            Type = row.Product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.Bundle
                    : row.Product.IsComponent ? MenuAuthoringCandidateTypes.Component : MenuAuthoringCandidateTypes.Product,
            Name = row.Product.Name,
            CategoryName = row.Product.ProductCategories.Where(link => link.IsPrimary && link.Category.IsActive)
                    .Select(link => link.Category.Name).FirstOrDefault(),
            ImageUrl = row.Product.ImageUrl ?? row.Product.Images.Where(image => image.IsPrimary)
                    .OrderBy(image => image.SortOrder).Select(image => image.Url).FirstOrDefault(),
            BasePrice = row.Product.BasePrice,
            ProductType = row.Product.Type,
            ParentOfferProductId = row.Product.MenuDefinition == null ? null : row.Product.MenuDefinition.ParentOfferProductId,
            ParentOfferVariationId = row.Product.MenuDefinition == null ? null : row.Product.MenuDefinition.ParentOfferVariationId,
            IsComponent = row.Product.IsComponent,
            IsActive = row.Product.IsActive,
            IsAvailable = row.Product.IsAvailable
        });

    private sealed class RankedProduct
    {
        public required Product Product { get; init; }
        public int Rank { get; init; }
    }
}
