using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public class ExplicitGroupPricingTests
{
    [Fact]
    public void IncludedUnitsAreWaivedInsideTheirOwnGroup()
    {
        var first = Ingredient("Emmental", 1m, 0);
        var second = Ingredient("Extra viande", 3m, 1);
        var group = Group(1, first, second);

        var price = Sut().CalculateIngredientCustomizationPrice(
            [first, second], [first.Id, second.Id], null, explicitGroups: [group]);

        price.Should().Be(1m, "the dearest selected unit is the included one");
    }

    [Fact]
    public void AllowancesDoNotLeakAcrossGroups()
    {
        var sauce = Ingredient("Sauce", 2m, 0);
        var cheese = Ingredient("Cheddar", 1m, 1);
        var sauceGroup = Group(1, sauce);
        var extrasGroup = Group(0, cheese);

        var price = Sut().CalculateIngredientCustomizationPrice(
            [sauce, cheese], [sauce.Id, cheese.Id], null,
            explicitGroups: [sauceGroup, extrasGroup]);

        price.Should().Be(1m);
    }

    private static BasketPricingService Sut() => new(
        new Mock<ICustomerDiscountService>(MockBehavior.Strict).Object,
        Options.Create(new OrderSettings()), NullLogger<BasketPricingService>.Instance);

    private static ProductIngredient Ingredient(string name, decimal price, int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Price = price,
        DisplayOrder = order,
        MaxQuantity = 3,
        IsOptional = true,
        IsActive = true,
        CreatedBy = "test"
    };

    private static ProductCustomizationGroup Group(
        int includedFree, params ProductIngredient[] ingredients)
    {
        var group = new ProductCustomizationGroup
        {
            Id = Guid.NewGuid(),
            Name = "Group",
            IsActive = true,
            IncludedFreeUnits = includedFree,
            MaxSelection = ingredients.Length,
            CreatedBy = "test"
        };
        foreach (var ingredient in ingredients)
        {
            group.IngredientOptions.Add(new ProductCustomizationIngredientOption
            {
                Id = Guid.NewGuid(),
                ProductCustomizationGroup = group,
                ProductCustomizationGroupId = group.Id,
                ProductIngredient = ingredient,
                ProductIngredientId = ingredient.Id,
                DisplayOrder = ingredient.DisplayOrder,
                CreatedBy = "test"
            });
        }
        return group;
    }
}
