using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus;

internal static class AttachedBundleChoiceOptionSetGuard
{
    private const string ConflictMessage =
        "This bundle section is attached to an option set. Change its selection rules or managed options through the option-set review and apply flow; translations remain editable.";

    public static async Task ValidatePatchAsync(
        ApplicationDbContext context,
        MenuDefinition definition,
        IReadOnlyCollection<MenuSectionDto> proposedSections,
        CancellationToken cancellationToken)
    {
        var sectionIds = definition.Sections.Select(section => section.Id).ToArray();
        if (sectionIds.Length == 0)
        {
            return;
        }

        var attachments = await context.OptionSetAttachments
            .AsNoTracking()
            .Include(attachment => attachment.AppliedRows)
            .Where(attachment => attachment.Role == OptionSetAttachmentRole.BundleChoice
                && attachment.TargetMenuSectionId != null
                && sectionIds.Contains(attachment.TargetMenuSectionId.Value))
            .ToListAsync(cancellationToken);

        if (attachments.Count == 0)
        {
            return;
        }

        await MenuSectionVariationValidator.ValidateReferencesAsync(
            context,
            IncludeCurrentItemsForValidation(definition, proposedSections),
            cancellationToken);

        var proposedById = new Dictionary<Guid, MenuSectionDto>();
        foreach (var proposed in proposedSections)
        {
            if (proposed.Id is Guid sectionId && !proposedById.TryAdd(sectionId, proposed))
            {
                // Leave duplicate-ID reporting to MenuSectionWriter so invalid legacy payloads
                // retain their established BadRequest response.
                return;
            }
        }

        foreach (var attachment in attachments)
        {
            var sectionId = attachment.TargetMenuSectionId!.Value;
            var currentSection = definition.Sections.FirstOrDefault(section => section.Id == sectionId);
            if (currentSection is null || !proposedById.TryGetValue(sectionId, out var proposedSection))
            {
                throw new ConflictException(ConflictMessage);
            }

            if (currentSection.IsRequired != proposedSection.IsRequired
                || currentSection.MinSelection != proposedSection.MinSelection
                || currentSection.MaxSelection != proposedSection.MaxSelection)
            {
                throw new ConflictException(ConflictMessage);
            }

            if (!proposedSection.ItemsSpecified)
            {
                continue;
            }

            if (attachment.AppliedRows.Any(row => row.RowType != nameof(MenuSectionItem)))
            {
                throw new ConflictException(ConflictMessage);
            }

            var managedRowIds = attachment.AppliedRows
                .Select(row => row.MaterializedRowId)
                .ToHashSet();
            if (managedRowIds.Count == 0)
            {
                continue;
            }

            var currentRows = currentSection.Items
                .Where(item => managedRowIds.Contains(item.Id))
                .ToDictionary(item => item.Id);
            var proposedRows = new Dictionary<Guid, MenuSectionItemDto>();
            foreach (var proposedRow in proposedSection.Items ?? [])
            {
                if (proposedRow.Id is Guid rowId && !proposedRows.TryAdd(rowId, proposedRow))
                {
                    // MenuSectionWriter owns the existing duplicate-option BadRequest contract.
                    return;
                }
            }

            foreach (var rowId in managedRowIds)
            {
                if (!currentRows.TryGetValue(rowId, out var currentRow)
                    || !proposedRows.TryGetValue(rowId, out var proposedRow)
                    || currentRow.ProductId != proposedRow.ProductId
                    || currentRow.ProductVariationId != proposedRow.ProductVariationId
                    || currentRow.AdditionalPrice != proposedRow.AdditionalPrice
                    || currentRow.DisplayOrder != proposedRow.DisplayOrder
                    || currentRow.IsDefault != proposedRow.IsDefault)
                {
                    throw new ConflictException(ConflictMessage);
                }
            }
        }
    }

    private static List<MenuSectionDto> IncludeCurrentItemsForValidation(
        MenuDefinition definition,
        IReadOnlyCollection<MenuSectionDto> proposedSections)
    {
        var currentSections = definition.Sections.ToDictionary(section => section.Id);
        return proposedSections.Select(proposed =>
        {
            if (proposed.ItemsSpecified || proposed.Id is not Guid sectionId
                || !currentSections.TryGetValue(sectionId, out var current))
            {
                return proposed;
            }

            return new MenuSectionDto
            {
                Id = proposed.Id,
                Name = proposed.Name,
                Description = proposed.Description,
                DisplayOrder = proposed.DisplayOrder,
                IsRequired = proposed.IsRequired,
                MinSelection = proposed.MinSelection,
                MaxSelection = proposed.MaxSelection,
                Items = current.Items.Select(item => new MenuSectionItemDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductVariationId = item.ProductVariationId,
                    AdditionalPrice = item.AdditionalPrice,
                    DisplayOrder = item.DisplayOrder,
                    IsDefault = item.IsDefault
                }).ToList()
            };
        }).ToList();
    }
}
