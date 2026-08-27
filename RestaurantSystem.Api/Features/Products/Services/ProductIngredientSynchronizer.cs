using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

/// <summary>
/// Reconciles a product's <see cref="ProductIngredient"/> rows with the list a PUT carries,
/// BY ID — the same contract the variation block of <c>UpdateProductCommand</c> has always had.
///
/// <para>
/// Why this exists at all: the update handler used to <c>RemoveRange</c> every ingredient of the
/// product and re-add each one, so every row got a NEW <see cref="Entity.Id"/> on every single
/// save, even when the admin changed nothing but the price. That id is not private to the product:
/// <c>OrderItem.IngredientQuantitiesJson</c> and <c>BasketItem.IngredientQuantitiesJson</c> are
/// <c>{ ingredientId: quantity }</c> maps, and <c>OrderMappingService.MapIngredientCustomizations</c>
/// resolves them by looking the id up in the CURRENT recipe. Re-keying the recipe therefore blanked
/// the ingredient detail of every past order of that product ("NO Onions" simply disappeared from
/// the kitchen ticket) and dropped the customisation of every live basket line. Diffing by id keeps
/// the ids stable, so the history keeps resolving.
/// </para>
///
/// <para>
/// The four cases, mirroring the variation block: a row whose id is absent from the payload is
/// REMOVED; a row whose id is supplied is UPDATED in place; an entry with no id is CREATED; and an
/// id that does not belong to this product is SKIPPED with a warning — it deletes nothing, because
/// removal is decided by the ids present on the product, never by the unknown one.
/// </para>
/// </summary>
internal static class ProductIngredientSynchronizer
{
    public static async Task SyncAsync(
        ApplicationDbContext context,
        Product product,
        IReadOnlyCollection<ProductIngredientDto> incoming,
        string auditIdentifier,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var incomingIds = incoming
            .Where(i => i.Id.HasValue)
            .Select(i => i.Id!.Value)
            .ToHashSet();

        // Remove ingredients not in the incoming list. Their descriptions go with them by cascade
        // (ProductIngredientConfiguration: Descriptions → DeleteBehavior.Cascade).
        var ingredientsToRemove = product.DetailedIngredients
            .Where(i => !incomingIds.Contains(i.Id))
            .ToList();
        context.ProductIngredients.RemoveRange(ingredientsToRemove);

        foreach (var ingredientDto in incoming)
        {
            ProductIngredient ingredient;

            if (ingredientDto.Id.HasValue)
            {
                var existing = product.DetailedIngredients
                    .FirstOrDefault(i => i.Id == ingredientDto.Id.Value);
                if (existing == null)
                {
                    // An id we do not own: skip it rather than silently minting a row under an id
                    // the caller chose. Nothing is deleted by this branch.
                    logger.LogWarning("Ingredient with ID {IngredientId} not found for product {ProductId}",
                        ingredientDto.Id.Value, product.Id);
                    continue;
                }

                ingredient = existing;
                ingredient.Name = ingredientDto.Name;
                ingredient.IsOptional = ingredientDto.IsOptional;
                ingredient.Price = ingredientDto.Price;
                ingredient.IsIncludedInBasePrice = ingredientDto.IsIncludedInBasePrice;
                ingredient.IsActive = ingredientDto.IsActive;
                ingredient.DisplayOrder = ingredientDto.DisplayOrder;
                ingredient.MaxQuantity = ingredientDto.MaxQuantity;
                ingredient.UpdatedAt = DateTime.UtcNow;
                ingredient.UpdatedBy = auditIdentifier;

                // Descriptions are replaced wholesale, exactly as the variation block does: the
                // payload's content map is the full set for this row.
                var existingDescriptions = await context.ProductIngredientDescriptions
                    .Where(d => d.ProductIngredientId == ingredient.Id)
                    .ToListAsync(cancellationToken);
                context.ProductIngredientDescriptions.RemoveRange(existingDescriptions);
            }
            else
            {
                ingredient = new ProductIngredient
                {
                    ProductId = product.Id,
                    Name = ingredientDto.Name,
                    IsOptional = ingredientDto.IsOptional,
                    Price = ingredientDto.Price,
                    IsIncludedInBasePrice = ingredientDto.IsIncludedInBasePrice,
                    IsActive = ingredientDto.IsActive,
                    DisplayOrder = ingredientDto.DisplayOrder,
                    MaxQuantity = ingredientDto.MaxQuantity,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = auditIdentifier
                };

                await context.ProductIngredients.AddAsync(ingredient, cancellationToken);
            }

            if (ingredientDto.Content == null)
            {
                continue;
            }

            foreach (var (languageCode, content) in ingredientDto.Content)
            {
                var description = new ProductIngredientDescription
                {
                    ProductIngredient = ingredient,
                    LanguageCode = languageCode,
                    Name = content.Name,
                    Description = content.Description,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = auditIdentifier
                };

                await context.ProductIngredientDescriptions.AddAsync(description, cancellationToken);
            }
        }
    }
}
