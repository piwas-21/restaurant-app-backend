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

        var proposedById = proposedSections
            .Where(section => section.Id.HasValue)
            .ToDictionary(section => section.Id.GetValueOrDefault());
        foreach (var attachment in attachments)
        {
            ValidateAttachment(definition, attachment, proposedById);
        }
    }

    private static void ValidateAttachment(
        MenuDefinition definition,
        OptionSetAttachment attachment,
        Dictionary<Guid, MenuSectionDto> proposedById)
    {
        var sectionId = attachment.TargetMenuSectionId.GetValueOrDefault();
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

        if (proposedSection.ItemsSpecified)
        {
            ValidateManagedRows(currentSection, proposedSection, attachment.AppliedRows);
        }
    }

    private static void ValidateManagedRows(
        MenuSection section,
        MenuSectionDto proposedSection,
        ICollection<OptionSetAppliedRow> appliedRows)
    {
        if (appliedRows.Any(row => row.RowType != nameof(MenuSectionItem)))
        {
            throw new ConflictException(ConflictMessage);
        }

        var managedRowIds = appliedRows.Select(row => row.MaterializedRowId).ToHashSet();
        if (managedRowIds.Count == 0)
        {
            return;
        }

        var currentRows = section.Items
            .Where(item => managedRowIds.Contains(item.Id))
            .ToDictionary(item => item.Id);
        var proposedRows = (proposedSection.Items ?? [])
            .Where(item => item.Id.HasValue)
            .ToDictionary(item => item.Id.GetValueOrDefault());
        foreach (var rowId in managedRowIds)
        {
            if (!currentRows.TryGetValue(rowId, out var currentRow)
                || !proposedRows.TryGetValue(rowId, out var proposedRow)
                || HasManagedRowChanges(currentRow, proposedRow))
            {
                throw new ConflictException(ConflictMessage);
            }
        }
    }

    private static bool HasManagedRowChanges(MenuSectionItem current, MenuSectionItemDto proposed) =>
        current.ProductId != proposed.ProductId
        || current.ProductVariationId != proposed.ProductVariationId
        || current.AdditionalPrice != proposed.AdditionalPrice
        || current.DisplayOrder != proposed.DisplayOrder
        || current.IsDefault != proposed.IsDefault;

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
