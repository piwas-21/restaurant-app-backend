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
        var hasAuthoredSideScreen = manifestSteps?.Any(step =>
            step.Kind == CustomerStepKind.BundleComponentSide
            && step.SectionItemId == sectionItemId
            && step.ProductId == component.Id) == true;
        var hasExplicitAssociationReference = selections?.Any(selection =>
            selection.SuggestedSideItemId.HasValue) == true;
        if (component.SuggestedSideItems.Count == 0
            && !hasAuthoredSideScreen
            && !hasExplicitAssociationReference)
            return [];

        if (selections is null or { Count: 0 })
        {
            if (component.SuggestedSideItems.Any(side => side.IsRequired))
                throw new BadRequestException("This component requires one or more suggested side items.");
            return [];
        }
        if (selections.Any(selection => selection.Quantity <= 0 || selection.Quantity > maxQuantityPerItem)
            || selections.Select(selection => selection.SuggestedSideItemId ?? selection.Id)
                .Distinct().Count() != selections.Count)
            throw new BadRequestException(
                $"Component side selections must be unique with quantities from 1 to {maxQuantityPerItem}.");

        var result = new List<(SelectedSideItemDto Selection, decimal UnitPrice)>();
        foreach (var selection in selections)
        {
            var candidates = component.SuggestedSideItems
                .Where(side => side.SideItemProductId == selection.Id && side.SideItemProduct is not null)
                .ToList();
            var membership = selection.SuggestedSideItemId is Guid associationId
                ? candidates.SingleOrDefault(side => side.Id == associationId)
                : candidates.Count == 1 ? candidates[0] : null;
            if (membership?.SideItemProduct is not { } sideProduct || !sideProduct.IsActive || !sideProduct.IsAvailable)
                throw new BadRequestException("A selected side is stale or ambiguous for this component.");

            BasketComponentGuard.EnsureNotOrderedAlone(sideProduct);
            BasketChannelGuard.EnsureOrderable(sideProduct, orderType);
            var variation = ResolveSideVariation(sideProduct, selection.ProductVariationId);
            var step = manifestSteps?.FirstOrDefault(candidate =>
                candidate.Kind == CustomerStepKind.BundleComponentSide
                && candidate.SectionItemId == sectionItemId
                && candidate.ProductId == component.Id
                && candidate.ScopeId == membership.Id);
            result.Add((selection with
            {
                SuggestedSideItemId = membership.Id,
                PresentationOrder = membership.DisplayOrder,
                CompositionRole = step?.CompositionRole ?? CompositionRole.Side,
            }, sideProduct.BasePrice + (variation?.PriceModifier ?? 0m)));
        }

        var selectedAssociationIds = result
            .Select(row => row.Selection.SuggestedSideItemId!.Value).ToHashSet();
        if (component.SuggestedSideItems.Any(side => side.IsRequired && !selectedAssociationIds.Contains(side.Id)))
            throw new BadRequestException("This component requires one or more suggested side items.");
        return result;
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
