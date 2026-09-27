using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetProductEntryValidator
{
    public static async Task ValidateAsync(
        ApplicationDbContext context,
        OptionSetEntryDto entry,
        OptionSetKind kind,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        if (entry.ProductId is not Guid productId || entry.GlobalIngredientId.HasValue)
        {
            throw new BadRequestException("Bundle-choice and suggested-side entries require a tenant product reference");
        }

        var product = await context.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        var isStagedProduct = stagedProductIds?.Contains(productId) == true;
        if (product is null || product.IsDeleted
            || (requireActiveReference && !isStagedProduct && (!product.IsActive || !product.IsAvailable))
            || (product.IsComponent && kind != OptionSetKind.BundleChoice))
        {
            throw new BadRequestException("Choose an active, available tenant product that is valid for this option-set kind");
        }

        if (entry.ProductVariationId is Guid variationId)
        {
            var variationExists = await context.ProductVariations.AnyAsync(
                variation => variation.Id == variationId && variation.ProductId == productId
                    && (!requireActiveReference || isStagedProduct || variation.IsActive),
                cancellationToken);
            if (!variationExists)
            {
                throw new BadRequestException("The selected active variation must belong to the referenced product");
            }
        }

        OptionSetProductEntryRules.ValidateDefaults(kind, entry);
    }
}
