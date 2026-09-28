using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// Validates variation references on menu section items before a bundle replace is persisted.
/// Foreign keys prove that an id exists, but they cannot prove that it belongs to the selected
/// option product, is live, or is orderable.
/// </summary>
public static class MenuSectionVariationValidator
{
    public static Task ValidateAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSectionDto> sections,
        CancellationToken cancellationToken) =>
        MenuSectionIntegrityRule.ValidateAsync(context, sections, cancellationToken);

    /// <summary>Preserve an omitted repeat setting on an unchanged legacy PUT snapshot.</summary>
    public static Task ValidateWithExistingAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSectionDto> sections,
        IEnumerable<MenuSection> existingSections,
        CancellationToken cancellationToken)
    {
        return ValidateAsync(context, ResolveExistingRepeatSettings(sections, existingSections), cancellationToken);
    }

    internal static IEnumerable<MenuSectionDto> ResolveExistingRepeatSettings(
        IEnumerable<MenuSectionDto> sections,
        IEnumerable<MenuSection> existingSections)
    {
        var existingById = existingSections.ToDictionary(section => section.Id);
        return sections.Select(section =>
            section.AllowRepeatedItems is null && section.Id is Guid id
                && existingById.TryGetValue(id, out var existing)
                ? section with { AllowRepeatedItems = existing.AllowRepeatedItems }
                : section);
    }

    public static Task ValidateReferencesAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSectionDto> sections,
        CancellationToken cancellationToken) =>
        MenuSectionIntegrityRule.ValidateReferencesAsync(context, sections, cancellationToken);

    public static Task ValidateEntitiesAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSection> sections,
        CancellationToken cancellationToken)
    {
        return ValidateAsync(context, MenuSectionIntegrityRule.Project(sections), cancellationToken);
    }
}
