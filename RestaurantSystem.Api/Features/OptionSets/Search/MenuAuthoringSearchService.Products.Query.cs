using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private IQueryable<RankedProduct> GetRankedProducts(
        string normalizedQuery,
        string searchText,
        OptionSetKind? forKind)
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

        var matching = MatchProducts(products, acceptedIds, rejectedIds, searchText);
        return RankProducts(matching);
    }

    private static IQueryable<ProductMatch> MatchProducts(
        IQueryable<Product> products,
        IQueryable<Guid> acceptedIds,
        IQueryable<Guid> rejectedIds,
        string searchText) => products.Select(product => new ProductMatch
        {
            Product = product,
            IsNameMatch = EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(product.Name),
                MenuAuthoringSearchDatabaseFunctions.Pattern(searchText), "\\"),
            IsAliasMatch = acceptedIds.Contains(product.Id),
            IsExactMatch = MenuAuthoringSearchDatabaseFunctions.Normalize(product.Name)
                == MenuAuthoringSearchDatabaseFunctions.Normalize(searchText),
            IsPrefixMatch = EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(product.Name),
                MenuAuthoringSearchDatabaseFunctions.PrefixPattern(searchText), "\\")
        })
            .Where(row => row.IsNameMatch || row.IsAliasMatch)
            .Where(row => !rejectedIds.Contains(row.Product.Id));

    private static IQueryable<RankedProduct> RankProducts(IQueryable<ProductMatch> matches) =>
        matches.Select(row => new RankedProduct
        {
            Product = row.Product,
            TypeRank = row.Product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.BundleRank
                : row.Product.IsComponent ? MenuAuthoringCandidateTypes.ComponentRank : MenuAuthoringCandidateTypes.ProductRank,
            RelevanceRank = row.IsNameMatch
                ? row.IsExactMatch ? MenuAuthoringCandidateTypes.ExactMatchRank
                    : row.IsPrefixMatch ? MenuAuthoringCandidateTypes.PrefixMatchRank
                    : MenuAuthoringCandidateTypes.NameMatchRank
                : MenuAuthoringCandidateTypes.AliasMatchRank
        });

    private sealed class ProductMatch
    {
        public required Product Product { get; init; }
        public bool IsNameMatch { get; init; }
        public bool IsAliasMatch { get; init; }
        public bool IsExactMatch { get; init; }
        public bool IsPrefixMatch { get; init; }
    }
}
