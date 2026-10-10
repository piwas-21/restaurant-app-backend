using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// The single server-side rule for menu-bundle selections. Basket adds and staff counter orders
/// both use this so required sections, selection limits, membership, and option prices cannot drift.
/// </summary>
public static class MenuBundleSelectionRules
{
    /// <summary>
    /// Validates every selected option against its exact section and returns the option surcharge
    /// for one bundle unit. Selection quantities are per bundle unit, as in <see
    /// cref="SelectedMenuOptionDto.Quantity"/>.
    /// </summary>
    public static decimal ValidateAndSumOptionPrices(
        IEnumerable<MenuSection> sections,
        IReadOnlyCollection<SelectedMenuOptionDto> selectedOptions)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(selectedOptions);

        var sectionList = sections.ToList();
        ValidateMembership(sectionList, selectedOptions);

        decimal optionsPrice = 0m;
        foreach (var section in sectionList)
        {
            var sectionSelections = selectedOptions
                .Where(option => option.SectionId == section.Id)
                .ToList();
            var selectionCount = section.AllowRepeatedItems
                ? sectionSelections.Sum(selection => (long)selection.Quantity)
                : sectionSelections.Count;

            if (section.IsRequired && selectionCount < section.MinSelection)
            {
                throw new BadRequestException(
                    $"Section '{section.Name}' requires at least {section.MinSelection} selection(s)");
            }

            if (selectionCount > section.MaxSelection)
            {
                throw new BadRequestException(
                    $"Section '{section.Name}' allows at most {section.MaxSelection} selection(s)");
            }

            optionsPrice += sectionSelections.Sum(selection =>
                PriceFor(ResolveSectionItem(
                    sectionList, selection.SectionId, selection.ItemId, selection.ProductVariationId,
                    selection.MenuSectionItemId))
                * selection.Quantity);
        }

        return optionsPrice;
    }

    /// <summary>
    /// Resolves an option through the section explicitly named by the request. Keeping this lookup
    /// beside the count rules prevents staff pricing from falling back to the first section that
    /// happens to contain the same product.
    /// </summary>
    public static MenuSectionItem ResolveSectionItem(
        IEnumerable<MenuSection> sections,
        Guid sectionId,
        Guid itemId,
        Guid? productVariationId = null,
        Guid? menuSectionItemId = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var section = sections.FirstOrDefault(candidate => candidate.Id == sectionId)
            ?? throw new BadRequestException($"Invalid section '{sectionId}' for this menu");

        if (menuSectionItemId.HasValue)
        {
            var selected = section.Items.FirstOrDefault(item => item.Id == menuSectionItemId.Value);
            if (selected is null || selected.ProductId != itemId
                || selected.ProductVariationId != productVariationId)
                throw new BadRequestException("The selected menu-section row does not match the requested option.");
            return selected;
        }

        var matches = section.Items.Where(item => item.ProductId == itemId
            && item.ProductVariationId == productVariationId).Take(2).ToList();
        if (matches.Count > 1)
            throw new BadRequestException("This menu option appears more than once in the section; send menuSectionItemId.");
        return matches.FirstOrDefault()
            ?? throw new NotFoundException($"Item not found in section '{section.Name}'");
    }

    /// <summary>Returns the section surcharge plus the fixed variation modifier, when present.</summary>
    public static decimal PriceFor(MenuSectionItem item)
    {
        if (item.ProductVariationId.HasValue && item.ProductVariation is null)
        {
            throw new BadRequestException(
                $"Variation '{item.ProductVariationId}' is not loaded for menu item '{item.ProductId}'");
        }

        if (item.ProductVariation is { } variation
            && (variation.ProductId != item.ProductId || variation.IsDeleted || !variation.IsActive))
        {
            throw new BadRequestException(
                $"Variation '{variation.Id}' is not active for menu item '{item.ProductId}'");
        }

        return item.AdditionalPrice + (item.ProductVariation?.PriceModifier ?? 0m);
    }

    private static void ValidateMembership(
        IReadOnlyCollection<MenuSection> sections,
        IReadOnlyCollection<SelectedMenuOptionDto> selectedOptions)
    {
        if (selectedOptions
            .GroupBy(selection => (selection.SectionId, selection.MenuSectionItemId,
                selection.ItemId, selection.ProductVariationId))
            .Any(group => group.Count() > 1))
        {
            throw new BadRequestException("A menu option can only be selected once per section");
        }

        foreach (var selection in selectedOptions)
        {
            var section = sections.FirstOrDefault(candidate => candidate.Id == selection.SectionId)
                ?? throw new BadRequestException($"Invalid section '{selection.SectionId}' for this menu");

            if (selection.Quantity < 1)
            {
                throw new BadRequestException(
                    $"Invalid quantity for item in section '{section.Name}'");
            }

            _ = ResolveSectionItem(
                sections, selection.SectionId, selection.ItemId, selection.ProductVariationId,
                selection.MenuSectionItemId);
        }
    }
}
