using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetReferenceBatchRules
{
    public static async Task<IReadOnlyList<string?>> ValidateIngredientsAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        IReadOnlyList<OptionSetEntryDto> entries,
        int maximumEntryCount,
        bool requireActiveReference,
        CancellationToken cancellationToken)
    {
        var safeEntries = GetBoundedEntries(entries, maximumEntryCount);
        var errors = new string?[safeEntries.Length];
        var ingredientIds = CollectIngredientIds(safeEntries, errors);
        var byId = await LoadIngredientsAsync(context, ingredientIds, cancellationToken);
        ValidateIngredientEntries(safeEntries, errors, byId, kind, requireActiveReference);
        return errors;
    }

    public static async Task<IReadOnlyList<string?>> ValidateProductsAsync(
        ApplicationDbContext context,
        IReadOnlyList<OptionSetEntryDto> entries,
        OptionSetKind kind,
        int maximumEntryCount,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var safeEntries = GetBoundedEntries(entries, maximumEntryCount);
        var errors = new string?[safeEntries.Length];
        var (productIds, variationIds) = CollectProductIds(safeEntries, errors);
        var products = await LoadProductsAsync(context, productIds, cancellationToken);
        var variations = await LoadVariationsAsync(context, variationIds, cancellationToken);
        ValidateProductEntries(safeEntries, errors, products, variations, kind, requireActiveReference, stagedProductIds);
        return errors;
    }

    private static OptionSetEntryDto[] GetBoundedEntries(
        IReadOnlyList<OptionSetEntryDto> entries,
        int maximumEntryCount)
    {
        if (entries.Count > maximumEntryCount)
        {
            throw new BadRequestException($"An option set may contain at most {maximumEntryCount} entries");
        }

        return entries.ToArray();
    }

    private static HashSet<Guid> CollectIngredientIds(OptionSetEntryDto[] entries, string?[] errors)
    {
        var ids = new HashSet<Guid>();
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            if (entry.GlobalIngredientId is not Guid id || entry.ProductId.HasValue || entry.ProductVariationId.HasValue)
            {
                errors[index] = "Ingredient and sauce entries require exactly one global ingredient reference";
                continue;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static async Task<Dictionary<Guid, GlobalIngredient>> LoadIngredientsAsync(
        ApplicationDbContext context,
        HashSet<Guid> ids,
        CancellationToken cancellationToken)
    {
        return ids.Count == 0
            ? []
            : await context.GlobalIngredients.AsNoTracking().Where(item => ids.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
    }

    private static void ValidateIngredientEntries(
        OptionSetEntryDto[] entries,
        string?[] errors,
        IReadOnlyDictionary<Guid, GlobalIngredient> ingredients,
        OptionSetKind kind,
        bool requireActiveReference)
    {
        var expectedKind = kind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            if (errors[index] is not null)
            {
                continue;
            }

            var ingredient = ingredients.GetValueOrDefault(entry.GlobalIngredientId!.Value);
            errors[index] = ValidateIngredientEntry(entry, ingredient, expectedKind, requireActiveReference);
        }
    }

    private static string? ValidateIngredientEntry(
        OptionSetEntryDto entry,
        GlobalIngredient? ingredient,
        IngredientKind expectedKind,
        bool requireActiveReference)
    {
        if (ingredient is null || ingredient.Kind != expectedKind
            || (requireActiveReference && (ingredient.ArchivedAt is not null || !ingredient.IsActive)))
        {
            return "Choose an active library ingredient of the same option-set kind";
        }

        if (!entry.IsOptional || entry.IsRequired || entry.IsDefault || entry.AdditionalPrice != 0m)
        {
            return "Ingredient and sauce entries cannot define required, default, or bundle-price rules";
        }

        if (entry.MaxQuantity < 1 || entry.Price < 0m)
        {
            return "Ingredient and sauce options need a positive quantity cap and non-negative price";
        }

        return null;
    }

    private static (HashSet<Guid> ProductIds, HashSet<Guid> VariationIds) CollectProductIds(
        OptionSetEntryDto[] entries,
        string?[] errors)
    {
        var productIds = new HashSet<Guid>();
        var variationIds = new HashSet<Guid>();
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            if (entry.ProductId is not Guid productId || entry.GlobalIngredientId.HasValue)
            {
                errors[index] = "Bundle-choice and suggested-side entries require a tenant product reference";
                continue;
            }

            productIds.Add(productId);
            if (entry.ProductVariationId is Guid variationId)
            {
                variationIds.Add(variationId);
            }
        }

        return (productIds, variationIds);
    }

    private static async Task<Dictionary<Guid, Product>> LoadProductsAsync(
        ApplicationDbContext context,
        HashSet<Guid> productIds,
        CancellationToken cancellationToken)
    {
        return productIds.Count == 0
            ? []
            : await context.Products.AsNoTracking().Where(item => productIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, ProductVariation>> LoadVariationsAsync(
        ApplicationDbContext context,
        HashSet<Guid> variationIds,
        CancellationToken cancellationToken)
    {
        return variationIds.Count == 0
            ? []
            : await context.ProductVariations.AsNoTracking().Where(item => variationIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
    }

    private static void ValidateProductEntries(
        OptionSetEntryDto[] entries,
        string?[] errors,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, ProductVariation> variations,
        OptionSetKind kind,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds)
    {
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            if (errors[index] is not null)
            {
                continue;
            }

            errors[index] = ValidateProductEntry(entry, products, variations, kind, requireActiveReference, stagedProductIds);
        }
    }

    private static string? ValidateProductEntry(
        OptionSetEntryDto entry,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, ProductVariation> variations,
        OptionSetKind kind,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds)
    {
        var productId = entry.ProductId!.Value;
        var product = products.GetValueOrDefault(productId);
        var isStagedProduct = stagedProductIds?.Contains(productId) == true;
        if (product is null || product.IsDeleted
            || (requireActiveReference && !isStagedProduct && (!product.IsActive || !product.IsAvailable))
            || (product.IsComponent && kind != OptionSetKind.BundleChoice))
        {
            return "Choose an active, available tenant product that is valid for this option-set kind";
        }

        if (entry.ProductVariationId is Guid variationId
            && !IsValidVariation(variations, variationId, productId, requireActiveReference, isStagedProduct))
        {
            return "The selected active variation must belong to the referenced product";
        }

        try
        {
            OptionSetProductEntryRules.ValidateDefaults(kind, entry);
            return null;
        }
        catch (BadRequestException exception)
        {
            return exception.Message;
        }
    }

    private static bool IsValidVariation(
        IReadOnlyDictionary<Guid, ProductVariation> variations,
        Guid variationId,
        Guid productId,
        bool requireActiveReference,
        bool isStagedProduct) =>
        variations.TryGetValue(variationId, out var variation)
        && variation.ProductId == productId
        && (!requireActiveReference || isStagedProduct || variation.IsActive);
}
