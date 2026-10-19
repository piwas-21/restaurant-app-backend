using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class CustomerCheckoutCompositionFixture(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    protected Guid _ingredientId;
    protected Guid _optionId;
    protected Guid _ingredientOptionId;
    protected Guid _groupId;
    protected Guid _extraId;
    protected Guid _bundleId;
    protected Guid _sectionId;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await context.Products.SingleAsync(item => item.Id == ProductId);
        var ingredient = new ProductIngredient
        {
            Product = product,
            ProductId = product.Id,
            Name = "Extra sauce",
            Price = 1.5m,
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 5,
            CreatedBy = "quote-fixture"
        };
        product.DetailedIngredients.Add(ingredient);
        var extra = new Product
        {
            Name = "Selected supplement",
            BasePrice = 19m,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "quote-fixture"
        };
        var group = new ProductCustomizationGroup
        {
            Product = product,
            ProductId = product.Id,
            Name = "Supplements",
            IsActive = true,
            MinSelection = 0,
            MaxSelection = 2,
            CreatedBy = "quote-fixture"
        };
        var option = new ProductCustomizationProductOption
        {
            ProductCustomizationGroup = group,
            ProductCustomizationGroupId = group.Id,
            OptionProduct = extra,
            OptionProductId = extra.Id,
            AdditionalPrice = 3m,
            CreatedBy = "quote-fixture"
        };
        var ingredientOption = new ProductCustomizationIngredientOption
        {
            ProductCustomizationGroup = group,
            ProductCustomizationGroupId = group.Id,
            ProductIngredient = ingredient,
            ProductIngredientId = ingredient.Id,
            CreatedBy = "quote-fixture"
        };
        group.IngredientOptions.Add(ingredientOption);
        group.ProductOptions.Add(option);
        product.CustomizationGroups.Add(group);
        var bundle = new Product
        {
            Name = "Quote bundle",
            BasePrice = 8m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "quote-fixture"
        };
        var definition = new MenuDefinition
        {
            Product = bundle,
            ProductId = bundle.Id,
            IsAlwaysAvailable = true,
            CreatedBy = "quote-fixture"
        };
        var section = new MenuSection
        {
            MenuDefinition = definition,
            MenuDefinitionId = definition.Id,
            Name = "Main",
            MinSelection = 1,
            MaxSelection = 2,
            IsRequired = true,
            CreatedBy = "quote-fixture"
        };
        section.Items.Add(new MenuSectionItem
        {
            MenuSection = section,
            MenuSectionId = section.Id,
            Product = product,
            ProductId = product.Id,
            AdditionalPrice = 2m,
            CreatedBy = "quote-fixture"
        });
        definition.Sections.Add(section);
        bundle.MenuDefinition = definition;
        context.Products.AddRange(extra, bundle);
        await context.SaveChangesAsync();
        _ingredientOptionId = ingredientOption.Id;
        (_ingredientId, _optionId, _groupId, _extraId, _bundleId, _sectionId) =
            (ingredient.Id, option.Id, group.Id, extra.Id, bundle.Id, section.Id);
    }

    protected List<CustomizationGroupSelectionDto> Options(bool selected, int sauces = 0)
    {
        var options = new List<CustomizationOptionSelectionDto>();
        if (selected) options.Add(new() { Kind = CustomizationOptionKind.Product, OptionId = _optionId });
        if (sauces > 0) options.Add(new() { Kind = CustomizationOptionKind.Ingredient, OptionId = _ingredientOptionId, Quantity = sauces });
        return [new() { GroupId = _groupId, Options = options }];
    }

    protected async Task<BasketDto> PrepareAsync(AddToBasketDto request) =>
        await BasketAsync(await SwitchAsync(Channel(await BasketAsync(await SendAsync(
            "/api/customer-mobile/basket/items", request)))));

}
