using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

internal static class BundleComponentSelection
{
    public static List<(SelectedSideItemDto Selection, decimal UnitPrice)> ResolveSides(
        Product component, Guid sectionItemId, List<SelectedSideItemDto>? selections,
        IReadOnlyList<CustomerStepManifestStepDto>? manifestSteps, OrderType? orderType,
        int maxQuantityPerItem)
    {
        if (CanIgnoreLegacySides(component, sectionItemId, selections, manifestSteps))
            return [];

        if (selections is null or { Count: 0 })
        {
            EnsureRequiredSidesWereSelected(component, new HashSet<Guid>());
            return [];
        }

        ValidateSideSelectionShape(selections, maxQuantityPerItem);
        var result = selections.Select(selection => ResolveSelectedSide(
            component, sectionItemId, selection, manifestSteps, orderType)).ToList();
        EnsureRequiredSidesWereSelected(component,
            result.Select(row => row.Selection.SuggestedSideItemId.GetValueOrDefault()).ToHashSet());
        return result;
    }

    private static bool CanIgnoreLegacySides(
        Product component, Guid sectionItemId, List<SelectedSideItemDto>? selections,
        IReadOnlyList<CustomerStepManifestStepDto>? manifestSteps)
    {
        var hasScreen = manifestSteps?.Any(step =>
            step.Kind == CustomerStepKind.BundleComponentSide
            && step.SectionItemId == sectionItemId
            && step.ProductId == component.Id) == true;
        var hasExplicitAssociation = selections?.Any(selection =>
            selection.SuggestedSideItemId.HasValue) == true;
        return component.SuggestedSideItems.Count == 0 && !hasScreen && !hasExplicitAssociation;
    }

    private static void ValidateSideSelectionShape(
        List<SelectedSideItemDto> selections, int maxQuantityPerItem)
    {
        var invalidQuantity = selections.Any(selection =>
            selection.Quantity <= 0 || selection.Quantity > maxQuantityPerItem);
        var duplicateAssociations = selections.Select(selection =>
                selection.SuggestedSideItemId ?? selection.Id)
            .Distinct().Count() != selections.Count;
        if (invalidQuantity || duplicateAssociations)
            throw new BadRequestException(
                $"Component side selections must be unique with quantities from 1 to {maxQuantityPerItem}.");
    }

    private static (SelectedSideItemDto Selection, decimal UnitPrice) ResolveSelectedSide(
        Product component, Guid sectionItemId, SelectedSideItemDto selection,
        IReadOnlyList<CustomerStepManifestStepDto>? manifestSteps, OrderType? orderType)
    {
        var candidates = component.SuggestedSideItems
            .Where(side => side.SideItemProductId == selection.Id && side.SideItemProduct is not null)
            .ToList();
        var membership = ResolveMembership(candidates, selection.SuggestedSideItemId);
        var sideProduct = membership?.SideItemProduct;
        if (membership is null || sideProduct is null || !sideProduct.IsActive || !sideProduct.IsAvailable)
            throw new BadRequestException("A selected side is stale or ambiguous for this component.");

        BasketComponentGuard.EnsureNotOrderedAlone(sideProduct);
        BasketChannelGuard.EnsureOrderable(sideProduct, orderType);
        var variation = ResolveSideVariation(sideProduct, selection.ProductVariationId);
        var step = FindSideStep(manifestSteps, sectionItemId, component.Id, membership.Id);
        var resolvedSelection = selection with
        {
            SuggestedSideItemId = membership.Id,
            PresentationOrder = membership.DisplayOrder,
            CompositionRole = step?.CompositionRole ?? CompositionRole.Side,
        };
        return (resolvedSelection, sideProduct.BasePrice + (variation?.PriceModifier ?? 0m));
    }

    private static ProductSideItem? ResolveMembership(
        List<ProductSideItem> candidates, Guid? associationId)
    {
        if (associationId.HasValue)
            return candidates.SingleOrDefault(side => side.Id == associationId.Value);
        if (candidates.Count == 1)
            return candidates[0];
        return null;
    }

    private static CustomerStepManifestStepDto? FindSideStep(
        IReadOnlyList<CustomerStepManifestStepDto>? manifestSteps,
        Guid sectionItemId, Guid productId, Guid associationId) =>
        manifestSteps?.FirstOrDefault(candidate =>
            candidate.Kind == CustomerStepKind.BundleComponentSide
            && candidate.SectionItemId == sectionItemId
            && candidate.ProductId == productId
            && candidate.ScopeId == associationId);

    private static void EnsureRequiredSidesWereSelected(Product component, HashSet<Guid> selectedAssociationIds)
    {
        if (component.SuggestedSideItems.Any(side =>
                side.IsRequired && !selectedAssociationIds.Contains(side.Id)))
            throw new BadRequestException("This component requires one or more suggested side items.");
    }

    public static ProductVariation? ResolveVariation(
        Product component, MenuSectionItem row, Guid? requestedVariationId)
    {
        if (row.ProductVariationId.HasValue && requestedVariationId.HasValue
            && row.ProductVariationId != requestedVariationId)
            throw new BadRequestException("A component variation cannot override the fixed menu-row variation.");

        var id = requestedVariationId ?? row.ProductVariationId;
        var variation = id.HasValue
            ? component.Variations.FirstOrDefault(candidate => candidate.Id == id.Value)
            : null;
        if (id.HasValue && (variation is null || variation.IsDeleted || !variation.IsActive))
            throw new BadRequestException("The selected component variation is not available.");
        BasketBaseProductGuard.EnsureVariationChosen(component, variation);
        return variation;
    }

    private static ProductVariation? ResolveSideVariation(Product product, Guid? variationId)
    {
        var variation = variationId.HasValue
            ? product.Variations.FirstOrDefault(row => row.Id == variationId.Value)
            : null;
        if (variationId.HasValue && (variation is null || !variation.IsActive || variation.IsDeleted))
            throw new BadRequestException("The selected side variation is not available.");
        BasketBaseProductGuard.EnsureVariationChosen(product, variation);
        return variation;
    }
}
