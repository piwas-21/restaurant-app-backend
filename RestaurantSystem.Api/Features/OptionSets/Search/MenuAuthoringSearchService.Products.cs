using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchProductsAsync(
        string pattern,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        HashSet<(string CandidateType, Guid CandidateId)> accepted,
        HashSet<(string CandidateType, Guid CandidateId)> rejected,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var acceptedIds = accepted.Where(item => item.CandidateType is MenuAuthoringCandidateTypes.Product
                or MenuAuthoringCandidateTypes.Component or MenuAuthoringCandidateTypes.Bundle)
            .Select(item => item.CandidateId).ToList();
        var rejectedIds = rejected.Where(item => item.CandidateType is MenuAuthoringCandidateTypes.Product
                or MenuAuthoringCandidateTypes.Component or MenuAuthoringCandidateTypes.Bundle)
            .Select(item => item.CandidateId).ToList();
        var products = _context.Products.AsNoTracking().Where(product => product.IsActive && product.IsAvailable);
        if (forKind == OptionSetKind.SuggestedSide)
        {
            products = products.Where(product => !product.IsComponent);
        }

        products = products.Where(product =>
            EF.Functions.ILike(product.Name, pattern, "\\") || acceptedIds.Contains(product.Id));
        if (rejectedIds.Count > 0)
        {
            products = products.Where(product => !rejectedIds.Contains(product.Id));
        }

        if (cursor is not null)
        {
            products = products.Where(product =>
                EF.Functions.Collate(product.Name, "C").CompareTo(cursor.Name) > 0
                || EF.Functions.Collate(product.Name, "C").CompareTo(cursor.Name) == 0
                && ((product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.BundleRank
                    : product.IsComponent ? MenuAuthoringCandidateTypes.ComponentRank : MenuAuthoringCandidateTypes.ProductRank) > cursor.TypeRank
                    || (product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.BundleRank
                        : product.IsComponent ? MenuAuthoringCandidateTypes.ComponentRank : MenuAuthoringCandidateTypes.ProductRank) == cursor.TypeRank
                    && product.Id.CompareTo(cursor.Id) > 0));
        }

        return await products
            .OrderBy(product => EF.Functions.Collate(product.Name, "C"))
            .ThenBy(product => product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.BundleRank
                : product.IsComponent ? MenuAuthoringCandidateTypes.ComponentRank : MenuAuthoringCandidateTypes.ProductRank)
            .ThenBy(product => product.Id)
            .Take(pageSize + 1)
            .Select(product => new MenuAuthoringSearchCandidateDto
            {
                Id = product.Id,
                Type = product.Type == ProductType.Menu ? MenuAuthoringCandidateTypes.Bundle
                    : product.IsComponent ? MenuAuthoringCandidateTypes.Component : MenuAuthoringCandidateTypes.Product,
                Name = product.Name,
                CategoryName = product.ProductCategories.Where(link => link.IsPrimary && link.Category.IsActive)
                    .Select(link => link.Category.Name).FirstOrDefault(),
                ImageUrl = product.ImageUrl ?? product.Images.Where(image => image.IsPrimary)
                    .OrderBy(image => image.SortOrder).Select(image => image.Url).FirstOrDefault(),
                BasePrice = product.BasePrice,
                ProductType = product.Type,
                ParentOfferProductId = product.MenuDefinition == null ? null : product.MenuDefinition.ParentOfferProductId,
                ParentOfferVariationId = product.MenuDefinition == null ? null : product.MenuDefinition.ParentOfferVariationId,
                IsComponent = product.IsComponent,
                IsActive = product.IsActive,
                IsAvailable = product.IsAvailable
            })
            .ToListAsync(cancellationToken);
    }
}
