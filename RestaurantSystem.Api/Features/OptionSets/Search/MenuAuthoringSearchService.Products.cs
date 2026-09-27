using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchProductsAsync(
        string normalizedQuery,
        string searchText,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
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

        var matching = products.Select(product => new
        {
            Product = product,
            SearchName = MenuAuthoringSearchDatabaseFunctions.Normalize(product.Name),
            SearchTerm = MenuAuthoringSearchDatabaseFunctions.Normalize(searchText),
            IsNameMatch = EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(product.Name),
                MenuAuthoringSearchDatabaseFunctions.Pattern(searchText), "\\"),
            IsAliasMatch = acceptedIds.Contains(product.Id)
        })
            .Where(row => row.IsNameMatch || row.IsAliasMatch)
            .Where(row => !rejectedIds.Contains(row.Product.Id));
        var ranked = matching.Select(row => new RankedProduct
        {
            Product = row.Product,
            TypeRank = row.Product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.BundleRank
                : row.Product.IsComponent ? MenuAuthoringCandidateTypes.ComponentRank : MenuAuthoringCandidateTypes.ProductRank,
            RelevanceRank = row.IsNameMatch
                ? row.SearchName == row.SearchTerm ? MenuAuthoringCandidateTypes.ExactMatchRank
                    : EF.Functions.ILike(row.SearchName,
                        MenuAuthoringSearchDatabaseFunctions.PrefixPattern(searchText), "\\")
                        ? MenuAuthoringCandidateTypes.PrefixMatchRank
                    : MenuAuthoringCandidateTypes.NameMatchRank
                : MenuAuthoringCandidateTypes.AliasMatchRank
        });

        if (cursor is not null)
        {
            ranked = AfterCursor(ranked, cursor);
        }

        return await ProjectProducts(ranked.OrderBy(row => row.RelevanceRank)
                .ThenBy(row => row.TypeRank)
                .ThenBy(row => EF.Functions.Collate(row.Product.Name, "C"))
                .ThenBy(row => row.Product.Id)
                .Take(pageSize + 1))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RankedProduct> AfterCursor(
        IQueryable<RankedProduct> products,
        MenuAuthoringSearchCursor cursor) =>
        products.Where(row => row.RelevanceRank > cursor.RelevanceRank
            || row.RelevanceRank == cursor.RelevanceRank
            && (row.TypeRank > cursor.TypeRank || row.TypeRank == cursor.TypeRank
                && (EF.Functions.Collate(row.Product.Name, "C").CompareTo(cursor.Name) > 0
                    || EF.Functions.Collate(row.Product.Name, "C").CompareTo(cursor.Name) == 0
                    && row.Product.Id.CompareTo(cursor.Id) > 0)));

    private static IQueryable<MenuAuthoringSearchCandidateDto> ProjectProducts(
        IQueryable<RankedProduct> products) => products.Select(row => new MenuAuthoringSearchCandidateDto
        {
            Id = row.Product.Id,
            Type = row.Product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.Bundle
            : row.Product.IsComponent ? MenuAuthoringCandidateTypes.Component : MenuAuthoringCandidateTypes.Product,
            Name = row.Product.Name,
            RelevanceRank = row.RelevanceRank,
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
        public int TypeRank { get; init; }
        public int RelevanceRank { get; init; }
    }
}
