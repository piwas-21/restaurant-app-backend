using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

internal static class BasketSideSelectionRules
{
    public static List<SelectedSideItemDto> Resolve(
        List<SelectedSideItemDto> selections,
        List<ProductSideItem> memberships,
        IReadOnlyList<CustomerStepManifestStepDto>? customerSteps)
    {
        var resolved = new List<SelectedSideItemDto>(selections.Count);
        foreach (var selection in selections)
        {
            var membership = ResolveSideMembership(selection, memberships);
            var legacyUnassociatedSelection = membership is null
                && selection.SuggestedSideItemId is null
                && memberships.Count == 0;
            if (membership is null && !legacyUnassociatedSelection)
                throw new BadRequestException("A selected side is stale or ambiguous; refresh the product and try again.");
            var step = FindRegularSideStep(customerSteps, membership?.Id);
            resolved.Add(selection with
            {
                SuggestedSideItemId = membership?.Id,
                PresentationOrder = membership?.DisplayOrder,
                CompositionRole = step?.CompositionRole
                    ?? (membership is null ? CompositionRole.Unknown : CompositionRole.Side),
            });
        }
        return resolved;
    }

    private static ProductSideItem? ResolveSideMembership(
        SelectedSideItemDto selection, List<ProductSideItem> memberships)
    {
        var candidates = memberships.Where(row => row.SideItemProductId == selection.Id).ToList();
        if (selection.SuggestedSideItemId is Guid associationId)
            return candidates.SingleOrDefault(row => row.Id == associationId);
        if (candidates.Count == 1)
            return candidates[0];
        return null;
    }

    private static CustomerStepManifestStepDto? FindRegularSideStep(
        IReadOnlyList<CustomerStepManifestStepDto>? steps, Guid? membershipId) =>
        steps?.FirstOrDefault(candidate =>
            membershipId.HasValue
            && candidate.Kind == CustomerStepKind.ProductSuggestedSide
            && candidate.TargetId == membershipId);

    public static void EnsureRequired(
        List<ProductSideItem> memberships,
        IReadOnlyList<SelectedSideItemDto> resolvedSelections)
    {
        var selectedIds = resolvedSelections.Where(selection => selection.SuggestedSideItemId.HasValue)
            .Select(selection => selection.SuggestedSideItemId.GetValueOrDefault()).ToHashSet();
        if (memberships.Any(row => row.IsRequired && !selectedIds.Contains(row.Id)))
            throw new BadRequestException("This product requires one or more suggested side items.");
    }

}
