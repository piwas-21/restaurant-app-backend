using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetIngredientEntryValidator
{
    public static async Task ValidateAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        OptionSetEntryDto entry,
        CancellationToken cancellationToken)
    {
        if (entry.GlobalIngredientId is not Guid id || entry.ProductId.HasValue || entry.ProductVariationId.HasValue)
        {
            throw new BadRequestException("Ingredient and sauce entries require exactly one global ingredient reference");
        }

        var expectedKind = kind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
        var ingredient = await context.GlobalIngredients.FirstOrDefaultAsync(
            item => item.Id == id && item.ArchivedAt == null, cancellationToken);
        if (ingredient is null || ingredient.Kind != expectedKind)
        {
            throw new BadRequestException("Choose an active library ingredient of the same option-set kind");
        }

        if (!entry.IsOptional || entry.IsRequired || entry.IsDefault || entry.AdditionalPrice != 0m)
        {
            throw new BadRequestException("Ingredient and sauce entries cannot define required, default, or bundle-price rules");
        }

        if (entry.MaxQuantity < 1 || entry.Price < 0m)
        {
            throw new BadRequestException("Ingredient and sauce options need a positive quantity cap and non-negative price");
        }
    }
}
