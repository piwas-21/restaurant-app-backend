using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerRuntimeRules
{
    public static async Task ValidateAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        OptionSetTargetState state,
        CancellationToken cancellationToken)
    {
        if (kind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            await ValidateIncludedDeductionAsync(context, state.Product, cancellationToken);
        }

        if (kind == OptionSetKind.BundleChoice && state.Section is not null)
        {
            await MenuSectionVariationValidator.ValidateEntitiesAsync(context, [state.Section], cancellationToken);
        }
    }

    private static async Task ValidateIncludedDeductionAsync(
        ApplicationDbContext context,
        Product product,
        CancellationToken cancellationToken)
    {
        await context.ProductIngredients.Where(ingredient => ingredient.ProductId == product.Id).LoadAsync(cancellationToken);
        var activeVariations = await context.ProductVariations
            .Where(variation => variation.ProductId == product.Id && variation.IsActive)
            .Select(variation => variation.PriceModifier).ToListAsync(cancellationToken);
        var ingredients = context.ProductIngredients.Local
            .Where(ingredient => ingredient.ProductId == product.Id)
            .Select(ingredient => new ProductIngredientDto
            {
                IsOptional = ingredient.IsOptional,
                IsIncludedInBasePrice = ingredient.IsIncludedInBasePrice,
                IsActive = ingredient.IsActive,
                Price = ingredient.Price
            }).ToList();
        var deduction = IncludedInBaseDeductionRule.MaxDeduction(ingredients);
        var minPrice = IncludedInBaseDeductionRule.MinEffectiveUnitPrice(
            product.BasePrice, product.HideBaseProduct, activeVariations);
        if (!IncludedInBaseDeductionRule.Fits(deduction, minPrice))
        {
            throw new BadRequestException(IncludedInBaseDeductionRule.BuildMessage(deduction, minPrice));
        }
    }
}
