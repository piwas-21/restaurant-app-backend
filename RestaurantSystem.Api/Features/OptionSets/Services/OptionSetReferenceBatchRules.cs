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
        bool requireActiveReference,
        CancellationToken cancellationToken)
    {
        var errors = new string?[entries.Count];
        var ids = new HashSet<Guid>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.GlobalIngredientId is not Guid id || entry.ProductId.HasValue || entry.ProductVariationId.HasValue)
            {
                errors[index] = "Ingredient and sauce entries require exactly one global ingredient reference";
                continue;
            }

            ids.Add(id);
        }

        var ingredients = ids.Count == 0
            ? []
            : await context.GlobalIngredients.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);
        var byId = ingredients.ToDictionary(item => item.Id);
        var expectedKind = kind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
        for (var index = 0; index < entries.Count; index++)
        {
            if (errors[index] is not null)
            {
                continue;
            }

            var entry = entries[index];
            var ingredient = byId.GetValueOrDefault(entry.GlobalIngredientId!.Value);
            if (ingredient is null || ingredient.Kind != expectedKind
                || (requireActiveReference && (ingredient.ArchivedAt is not null || !ingredient.IsActive)))
            {
                errors[index] = "Choose an active library ingredient of the same option-set kind";
                continue;
            }

            if (!entry.IsOptional || entry.IsRequired || entry.IsDefault || entry.AdditionalPrice != 0m)
            {
                errors[index] = "Ingredient and sauce entries cannot define required, default, or bundle-price rules";
            }

            if (entry.MaxQuantity < 1 || entry.Price < 0m)
            {
                errors[index] ??= "Ingredient and sauce options need a positive quantity cap and non-negative price";
            }
        }

        return errors;
    }

    public static async Task<IReadOnlyList<string?>> ValidateProductsAsync(
        ApplicationDbContext context,
        IReadOnlyList<OptionSetEntryDto> entries,
        OptionSetKind kind,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        var errors = new string?[entries.Count];
        var productIds = new HashSet<Guid>();
        var variationIds = new HashSet<Guid>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
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

        Dictionary<Guid, Product> products = productIds.Count == 0
            ? []
            : await context.Products.Where(item => productIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
        Dictionary<Guid, ProductVariation> variations = variationIds.Count == 0
            ? []
            : await context.ProductVariations.Where(item => variationIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);

        for (var index = 0; index < entries.Count; index++)
        {
            if (errors[index] is not null)
            {
                continue;
            }

            var entry = entries[index];
            var productId = entry.ProductId!.Value;
            var product = products.GetValueOrDefault(productId);
            var isStagedProduct = stagedProductIds?.Contains(productId) == true;
            if (product is null || product.IsDeleted
                || (requireActiveReference && !isStagedProduct && (!product.IsActive || !product.IsAvailable))
                || (product.IsComponent && kind != OptionSetKind.BundleChoice))
            {
                errors[index] = "Choose an active, available tenant product that is valid for this option-set kind";
                continue;
            }

            if (entry.ProductVariationId is Guid variationId
                && (!variations.TryGetValue(variationId, out var variation)
                    || variation.ProductId != productId
                    || (requireActiveReference && !isStagedProduct && !variation.IsActive)))
            {
                errors[index] = "The selected active variation must belong to the referenced product";
                continue;
            }

            try
            {
                OptionSetProductEntryRules.ValidateDefaults(kind, entry);
            }
            catch (BadRequestException exception)
            {
                errors[index] = exception.Message;
            }
        }

        return errors;
    }
}
