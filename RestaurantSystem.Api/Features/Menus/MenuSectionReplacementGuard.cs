using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus;

internal static class MenuSectionReplacementGuard
{
    private const string VersionedEditConflictMessage =
        "This menu has entered versioned section editing. Reload it; unchanged sections may accompany other edits, but section changes must use PATCH /api/Menus/{id}/sections with the current If-Match ETag.";

    public static bool ShouldReplaceSections(
        MenuDefinition definition,
        IReadOnlyCollection<MenuSectionDto> proposedSections)
    {
        if (!definition.VersionedSectionEditingStarted)
        {
            return true;
        }

        return IsUnchangedSnapshot(definition.Sections, proposedSections)
            ? false
            : throw new ConflictException(VersionedEditConflictMessage);
    }

    internal static bool IsUnchangedSnapshot(
        ICollection<MenuSection> currentSections,
        IReadOnlyCollection<MenuSectionDto> proposedSections)
    {
        if (currentSections.Count != proposedSections.Count)
        {
            return false;
        }

        var proposedById = new Dictionary<Guid, MenuSectionDto>();
        foreach (var section in proposedSections)
        {
            if (section.Id is not Guid sectionId || !proposedById.TryAdd(sectionId, section))
            {
                return false;
            }
        }

        foreach (var current in currentSections)
        {
            if (!proposedById.TryGetValue(current.Id, out var proposed)
                || current.Name != proposed.Name
                || current.Description != proposed.Description
                || current.DisplayOrder != proposed.DisplayOrder
                || current.IsRequired != proposed.IsRequired
                || current.MinSelection != proposed.MinSelection
                || current.MaxSelection != proposed.MaxSelection
                || current.AllowRepeatedItems != (proposed.AllowRepeatedItems ?? current.AllowRepeatedItems)
                || !ItemsMatchWhenSpecified(current.Items, proposed)
                || !TranslationsMatchWhenSpecified(current.Translations, proposed))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsTranslationOnlyChange(
        ICollection<MenuSection> currentSections,
        IReadOnlyCollection<MenuSectionDto> proposedSections)
    {
        if (currentSections.Count != proposedSections.Count)
        {
            return false;
        }

        var byId = currentSections.ToDictionary(section => section.Id);
        var seen = new HashSet<Guid>();
        var changed = false;
        foreach (var proposed in proposedSections)
        {
            if (proposed.Id is not Guid id || !seen.Add(id) ||
                !byId.TryGetValue(id, out var current) ||
                current.Name != proposed.Name || current.Description != proposed.Description ||
                current.DisplayOrder != proposed.DisplayOrder ||
                current.IsRequired != proposed.IsRequired ||
                current.MinSelection != proposed.MinSelection ||
                current.MaxSelection != proposed.MaxSelection ||
                current.AllowRepeatedItems != (proposed.AllowRepeatedItems ?? current.AllowRepeatedItems) ||
                !ItemsMatchWhenSpecified(current.Items, proposed))
            {
                return false;
            }

            changed |= !TranslationsMatchWhenSpecified(current.Translations, proposed);
        }

        return changed;
    }

    private static bool ItemsMatchWhenSpecified(
        ICollection<MenuSectionItem> currentItems,
        MenuSectionDto proposed)
    {
        if (!proposed.ItemsSpecified)
        {
            return true;
        }

        var proposedItems = proposed.Items ?? [];
        if (currentItems.Count != proposedItems.Count)
        {
            return false;
        }

        var current = currentItems
            .Select(item => (item.ProductId, item.ProductVariationId, item.AdditionalPrice, item.DisplayOrder, item.IsDefault))
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.ProductId)
            .ThenBy(item => item.ProductVariationId)
            .ToArray();
        var proposedSnapshot = proposedItems
            .Select(item => (item.ProductId, item.ProductVariationId, item.AdditionalPrice, item.DisplayOrder, item.IsDefault))
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.ProductId)
            .ThenBy(item => item.ProductVariationId)
            .ToArray();

        return current.SequenceEqual(proposedSnapshot);
    }

    private static bool TranslationsMatchWhenSpecified(
        ICollection<MenuSectionTranslation> currentTranslations,
        MenuSectionDto proposed)
    {
        if (!proposed.TranslationsSpecified)
        {
            return true;
        }

        var proposedTranslations = proposed.Translations ?? [];
        if (currentTranslations.Count != proposedTranslations.Count)
        {
            return false;
        }

        foreach (var (language, translation) in proposedTranslations)
        {
            var current = currentTranslations.FirstOrDefault(row =>
                string.Equals(row.LanguageCode, language.Trim(), StringComparison.OrdinalIgnoreCase));
            if (current is null
                || current.Name != translation.Name.Trim()
                || current.Description != NormalizeDescription(translation.Description))
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
