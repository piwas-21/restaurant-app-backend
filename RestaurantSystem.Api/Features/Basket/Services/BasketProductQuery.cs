using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>Loads the product graph required by the shared basket item factory.</summary>
public static class BasketProductQuery
{
    public static IQueryable<Product> WithFactoryDependencies(IQueryable<Product> products) => products
        .AsSplitQuery()
        .Include(product => product.Variations)
        .Include(product => product.DetailedIngredients)
        .Include(product => product.CustomizationGroups)
            .ThenInclude(group => group.IngredientOptions)
                .ThenInclude(option => option.ProductIngredient)
        .Include(product => product.CustomizationGroups)
            .ThenInclude(group => group.ProductOptions)
                .ThenInclude(option => option.OptionProduct.ProductCategories)
                    .ThenInclude(category => category.Category)
        .Include(product => product.ProductCategories)
            .ThenInclude(category => category.Category)
        .Include(product => product.MenuDefinition)
            .ThenInclude(definition => definition!.Sections)
                .ThenInclude(section => section.Items)
                    .ThenInclude(item => item.Product)
        .Include(product => product.MenuDefinition!.Sections)
            .ThenInclude(section => section.Items)
                .ThenInclude(item => item.ProductVariation);
}
