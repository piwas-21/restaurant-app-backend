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
            var selectionCount = sectionSelections.Count;

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
                ResolveSectionItem(sectionList, selection.SectionId, selection.ItemId).AdditionalPrice
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
        IEnumerable<MenuSection> sections, Guid sectionId, Guid itemId)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var section = sections.FirstOrDefault(candidate => candidate.Id == sectionId)
            ?? throw new BadRequestException($"Invalid section '{sectionId}' for this menu");

        return section.Items.FirstOrDefault(item => item.ProductId == itemId)
            ?? throw new NotFoundException($"Item not found in section '{section.Name}'");
    }

    private static void ValidateMembership(
        IReadOnlyCollection<MenuSection> sections,
        IReadOnlyCollection<SelectedMenuOptionDto> selectedOptions)
    {
        foreach (var selection in selectedOptions)
        {
            var section = sections.FirstOrDefault(candidate => candidate.Id == selection.SectionId)
                ?? throw new BadRequestException($"Invalid section '{selection.SectionId}' for this menu");

            if (selection.Quantity < 1)
            {
                throw new BadRequestException(
                    $"Invalid quantity for item in section '{section.Name}'");
            }

            _ = ResolveSectionItem(sections, selection.SectionId, selection.ItemId);
        }
    }
}
