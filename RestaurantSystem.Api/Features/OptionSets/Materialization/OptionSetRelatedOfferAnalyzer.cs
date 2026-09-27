using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetRelatedOfferAnalyzer
{
    public static async Task<IReadOnlyList<OptionSetRelatedOfferWarningDto>> AnalyzeAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        if (set.Kind != OptionSetKind.BundleChoice)
        {
            return [];
        }

        var warnings = new List<OptionSetRelatedOfferWarningDto>();
        foreach (var target in request.Targets.Where(IsChoiceTarget))
        {
            var source = await LoadSourceAsync(context, set, target, cancellationToken);
            if (source is null || source.State.Settings.MinSelection <= 0)
            {
                continue;
            }

            var offerRootId = target.Role == OptionSetAttachmentRole.ProductChoice
                ? source.State.Product.Id
                : source.State.MenuDefinition?.ParentOfferProductId;
            if (offerRootId is null)
            {
                continue;
            }

            var candidates = await LoadRelatedTargetsAsync(
                context,
                offerRootId.Value,
                source.State.MenuDefinition?.ParentOfferVariationId,
                source.State.SelectedEntries,
                cancellationToken);
            var attachments = await context.OptionSetAttachments.AsNoTracking()
                .Where(item => item.OptionSetId == set.Id && item.MinSelection > 0
                    && (item.TargetProductId == offerRootId
                        || item.TargetProductId == source.State.Product.Id
                        || candidates.Select(candidate => candidate.ProductId).Contains(item.TargetProductId)))
                .Select(item => new ExistingAttachment(
                    item.TargetProductId, item.TargetMenuSectionId, item.TargetCustomizationGroupId, item.Role))
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                if (HasRequiredAttachment(candidate, attachments)
                    || await IsRequiredRequestTargetAsync(context, set, request, candidate, cancellationToken))
                {
                    continue;
                }

                warnings.Add(new OptionSetRelatedOfferWarningDto
                {
                    TargetKey = target.TargetKey,
                    RelatedProductId = candidate.ProductId,
                    RelatedProductName = candidate.ProductName,
                    RelatedOfferType = candidate.Role == OptionSetAttachmentRole.ProductChoice ? "standalone" : "menu",
                    RelatedVariationId = candidate.ParentVariationId,
                    RelatedTargetRole = candidate.Role == OptionSetAttachmentRole.ProductChoice ? "productChoice" : "bundleChoice",
                    RelatedTargetId = candidate.TargetId,
                    RelatedTargetName = candidate.TargetName,
                    ReasonRequired = string.IsNullOrWhiteSpace(target.IntentionalDifferenceReason)
                        && string.IsNullOrWhiteSpace(source.State.Attachment?.IntentionalDifferenceReason)
                });
            }
        }

        return warnings
            .DistinctBy(warning => (warning.TargetKey, warning.RelatedProductId, warning.RelatedTargetId))
            .ToList();
    }

    private static bool IsChoiceTarget(OptionSetMaterializationTargetRequest target) =>
        target.Role is OptionSetAttachmentRole.ProductChoice or OptionSetAttachmentRole.BundleChoice;

    private static async Task<SourceTarget?> LoadSourceAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await OptionSetMaterializerTargetLoader.LoadAsync(context, set, target, null, cancellationToken);
            return new SourceTarget(state);
        }
        catch (ConflictException)
        {
            return null;
        }
    }

    private static async Task<List<RelatedTarget>> LoadRelatedTargetsAsync(
        ApplicationDbContext context,
        Guid offerRootId,
        Guid? parentVariationId,
        IReadOnlyCollection<OptionSetEntry> selectedEntries,
        CancellationToken cancellationToken)
    {
        var productIds = selectedEntries.Select(entry => entry.ProductId).OfType<Guid>().ToHashSet();
        var rootProduct = await context.Products.AsNoTracking()
            .Where(product => product.Id == offerRootId && product.Type != ProductType.Menu
                && !product.IsDeleted && product.IsActive)
            .Select(product => new RelatedTarget(product.Id, product.Name, OptionSetAttachmentRole.ProductChoice,
                null, null, null))
            .SingleOrDefaultAsync(cancellationToken);
        var targets = new List<RelatedTarget>();
        if (rootProduct is not null)
        {
            var groups = await context.ProductCustomizationGroups.AsNoTracking()
                .Where(group => group.ProductId == offerRootId
                    && group.IsActive
                    && group.ProductOptions.Any(option => productIds.Contains(option.OptionProductId)))
                .OrderBy(group => group.DisplayOrder)
                .Select(group => new RelatedTarget(
                    offerRootId, group.Product.Name, OptionSetAttachmentRole.ProductChoice,
                    null, group.Id, group.Name))
                .ToListAsync(cancellationToken);
            targets.AddRange(groups.Count == 0 ? [rootProduct] : groups);
        }

        var menuProducts = await context.MenuDefinitions.AsNoTracking()
            .Where(definition => definition.ParentOfferProductId == offerRootId
                && definition.ParentOfferVariationId == parentVariationId
                && definition.Product.IsActive && !definition.Product.IsDeleted)
            .Select(definition => new MenuProduct(
                definition.ProductId, definition.Product.Name, definition.ParentOfferVariationId))
            .ToListAsync(cancellationToken);
        if (menuProducts.Count == 0)
        {
            return targets;
        }

        var menuIds = menuProducts.Select(product => product.ProductId).ToHashSet();
        var sections = await context.MenuSections.AsNoTracking()
            .Where(section => menuIds.Contains(section.MenuDefinition.ProductId)
                && section.Items.Any(item => productIds.Contains(item.ProductId)))
            .OrderBy(section => section.DisplayOrder)
            .Select(section => new RelatedSection(
                section.Id,
                section.MenuDefinition.ProductId,
                section.MenuDefinition.Product.Name,
                section.MenuDefinition.ParentOfferVariationId,
                section.Name))
            .ToListAsync(cancellationToken);
        foreach (var product in menuProducts)
        {
            var matchingSections = sections.Where(section => section.ProductId == product.ProductId)
                .Select(section => new RelatedTarget(
                    product.ProductId, product.Name, OptionSetAttachmentRole.BundleChoice,
                    product.ParentVariationId, section.Id, section.Name))
                .ToList();
            targets.AddRange(matchingSections.Count == 0
                ? [new RelatedTarget(product.ProductId, product.Name, OptionSetAttachmentRole.BundleChoice,
                    product.ParentVariationId, null, null)]
                : matchingSections);
        }

        return targets;
    }

    private static bool HasRequiredAttachment(
        RelatedTarget candidate,
        IReadOnlyCollection<ExistingAttachment> attachments) => attachments.Any(item =>
        item.ProductId == candidate.ProductId
        && item.Role == candidate.Role
        && (candidate.Role == OptionSetAttachmentRole.ProductChoice
            ? candidate.TargetId is null || item.CustomizationGroupId == candidate.TargetId
            : candidate.TargetId is null || item.MenuSectionId == candidate.TargetId));

    private static async Task<bool> IsRequiredRequestTargetAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationRequest request,
        RelatedTarget candidate,
        CancellationToken cancellationToken)
    {
        var targets = request.Targets.Where(target => target.TargetProductId == candidate.ProductId
            && target.Role == candidate.Role
            && (candidate.TargetId is null
                || target.TargetCustomizationGroupId == candidate.TargetId
                || target.TargetMenuSectionId == candidate.TargetId));
        foreach (var target in targets)
        {
            var state = await LoadSourceAsync(context, set, target, cancellationToken);
            if (state?.State.Settings.MinSelection > 0)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record SourceTarget(OptionSetTargetState State);
    private sealed record MenuProduct(Guid ProductId, string Name, Guid? ParentVariationId);
    private sealed record RelatedSection(Guid Id, Guid ProductId, string ProductName, Guid? ParentVariationId, string Name);
    private sealed record RelatedTarget(
        Guid ProductId,
        string ProductName,
        OptionSetAttachmentRole Role,
        Guid? ParentVariationId,
        Guid? TargetId,
        string? TargetName);
    private sealed record ExistingAttachment(
        Guid ProductId,
        Guid? MenuSectionId,
        Guid? CustomizationGroupId,
        OptionSetAttachmentRole Role);
}
