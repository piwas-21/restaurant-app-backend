using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static partial class OptionSetRelatedOfferAnalyzer
{
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
