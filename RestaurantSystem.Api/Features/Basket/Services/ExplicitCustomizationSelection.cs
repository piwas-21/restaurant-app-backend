using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.Basket.Services;

public sealed record ResolvedProductCustomization(
    Guid MembershipId,
    Product Product,
    decimal AdditionalPrice,
    int Quantity);

public sealed record ExplicitCustomizationResolution(
    List<Guid> SelectedIngredientIds,
    Dictionary<Guid, int> IngredientQuantities,
    List<ResolvedProductCustomization> ProductOptions);

public static class ExplicitCustomizationSelection
{
    public static void EnsurePersisted(
        Product product, BasketItem row, IReadOnlyCollection<BasketItem> children)
    {
        if (!product.CustomizationGroups.Any(group => group.IsActive))
            return;

        Dictionary<Guid, int>? quantities;
        try
        {
            quantities = string.IsNullOrWhiteSpace(row.IngredientQuantitiesJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<Guid, int>>(row.IngredientQuantitiesJson);
        }
        catch (JsonException)
        {
            throw new BadRequestException("Stored customization quantities are unreadable");
        }

        var selections = product.CustomizationGroups.Where(group => group.IsActive)
            .Select(group => new CustomizationGroupSelectionDto
            {
                GroupId = group.Id,
                Options = group.IngredientOptions
                    .Where(option => row.SelectedIngredients?.Contains(option.ProductIngredientId) == true)
                    .Select(option => new CustomizationOptionSelectionDto
                    {
                        Kind = CustomizationOptionKind.Ingredient,
                        OptionId = option.Id,
                        Quantity = quantities?.GetValueOrDefault(option.ProductIngredientId) ?? 1
                    })
                    .Concat(group.ProductOptions
                        .Where(option => children.Any(child => child.ProductCustomizationOptionId == option.Id))
                        .Select(option => new CustomizationOptionSelectionDto
                        {
                            Kind = CustomizationOptionKind.Product,
                            OptionId = option.Id,
                            Quantity = 1
                        }))
                    .ToList()
            }).ToList();

        Resolve(product, selections);
    }

    public static ExplicitCustomizationResolution Resolve(
        Product product,
        IReadOnlyCollection<CustomizationGroupSelectionDto>? requested)
    {
        var groups = product.CustomizationGroups.Where(group => group.IsActive)
            .ToDictionary(group => group.Id);
        if (groups.Count == 0)
        {
            if (requested is { Count: > 0 })
                throw new BadRequestException("This product has no explicit customization groups");
            return new([], [], []);
        }

        if (requested == null)
            throw new BadRequestException("Customization selections are required for this product");
        if (requested.Select(selection => selection.GroupId).Distinct().Count() != requested.Count)
            throw new BadRequestException("A customization group may be submitted only once");

        var ingredientIds = new List<Guid>();
        var quantities = new Dictionary<Guid, int>();
        var productOptions = new List<ResolvedProductCustomization>();

        foreach (var selection in requested)
        {
            if (!groups.TryGetValue(selection.GroupId, out var group))
                throw new BadRequestException("A selected customization group is unavailable");
            ResolveGroup(group, selection.Options, ingredientIds, quantities, productOptions);
        }

        foreach (var omitted in groups.Values.Where(group => requested.All(x => x.GroupId != group.Id)))
            EnsureCardinality(omitted, 0);

        return new(ingredientIds, quantities, productOptions);
    }

    private static void ResolveGroup(
        ProductCustomizationGroup group,
        List<CustomizationOptionSelectionDto> requested,
        List<Guid> ingredientIds,
        Dictionary<Guid, int> quantities,
        List<ResolvedProductCustomization> productOptions)
    {
        if (requested.Select(option => (option.Kind, option.OptionId)).Distinct().Count() != requested.Count)
            throw new BadRequestException("A customization option may be submitted only once");
        EnsureCardinality(group, requested.Count);

        foreach (var requestedOption in requested)
        {
            if (requestedOption.Quantity <= 0)
                throw new BadRequestException("Customization option quantities must be positive");

            if (requestedOption.Kind == CustomizationOptionKind.Ingredient)
                ResolveIngredient(group, requestedOption, ingredientIds, quantities);
            else if (requestedOption.Kind == CustomizationOptionKind.Product)
                ResolveProduct(group, requestedOption, productOptions);
            else
                throw new BadRequestException("Unknown customization option kind");
        }
    }

    private static void ResolveIngredient(
        ProductCustomizationGroup group,
        CustomizationOptionSelectionDto requested,
        List<Guid> ingredientIds,
        Dictionary<Guid, int> quantities)
    {
        var membership = group.IngredientOptions.FirstOrDefault(option => option.Id == requested.OptionId)
            ?? throw new BadRequestException("An ingredient option does not belong to the selected group");
        var ingredient = membership.ProductIngredient;
        if (!ingredient.IsActive || requested.Quantity > ingredient.MaxQuantity)
            throw new BadRequestException("An ingredient option is unavailable or exceeds its quantity limit");
        ingredientIds.Add(ingredient.Id);
        quantities.Add(ingredient.Id, requested.Quantity);
    }

    private static void ResolveProduct(
        ProductCustomizationGroup group,
        CustomizationOptionSelectionDto requested,
        List<ResolvedProductCustomization> productOptions)
    {
        var membership = group.ProductOptions.FirstOrDefault(option => option.Id == requested.OptionId)
            ?? throw new BadRequestException("A product option does not belong to the selected group");
        if (requested.Quantity != 1 || !membership.OptionProduct.IsActive
            || !membership.OptionProduct.IsAvailable || membership.OptionProduct.IsDeleted)
            throw new BadRequestException("A product option is unavailable or has an invalid quantity");
        productOptions.Add(new(membership.Id, membership.OptionProduct,
            membership.AdditionalPrice, requested.Quantity));
    }

    private static void EnsureCardinality(ProductCustomizationGroup group, int selected)
    {
        if (selected < group.MinSelection || selected > group.MaxSelection)
            throw new BadRequestException(
                $"Customization group '{group.Name}' requires {group.MinSelection} to {group.MaxSelection} selections");
    }
}
