using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public class SauceSelectionRuleTests
{
    [Fact]
    public void ThreeDistinctSauces_AtTheMaximum_AreAllowed()
    {
        var rows = Rows(4, 4);
        var action = () => SauceSelectionRule.EnsureWithinMaximum(rows, rows.Take(3).Select(row => row.Id).ToList(), 3);
        action.Should().NotThrow();
    }

    [Fact]
    public void FourDistinctSauces_OverTheMaximum_ReturnsStableCodeAndMessage()
    {
        var rows = Rows(4, 0);
        var action = () => SauceSelectionRule.EnsureWithinMaximum(rows, rows.Select(row => row.Id).ToList(), 3);

        var exception = action.Should().Throw<BadRequestException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.SauceMaximumExceeded);
        exception.Message.Should().Be(SauceSelectionRule.MaximumExceededMessage);
    }

    [Fact]
    public void RepeatedIds_AreOneDistinctSauceRow()
    {
        var rows = Rows(1, 0);
        var action = () => SauceSelectionRule.EnsureWithinMaximum(rows, [rows[0].Id, rows[0].Id, rows[0].Id], 1);
        action.Should().NotThrow();
    }

    [Fact]
    public void FourNonSauceRows_DoNotUseTheSauceLimit()
    {
        var rows = Rows(0, 4);
        var action = () => SauceSelectionRule.EnsureWithinMaximum(rows, rows.Select(row => row.Id).ToList(), 0);
        action.Should().NotThrow();
    }

    [Fact]
    public void MinimumEnforcementDisabled_PreservesLegacyNoSelectionBehavior()
    {
        var sauces = Rows(2, 0);

        var action = () => SauceSelectionRule.EnsureAtLeastMinimum(
            sauces, selectedIngredientIds: null, ingredientQuantities: null, sauceMin: 2,
            enforcementEnabled: false);

        action.Should().NotThrow();
    }

    [Fact]
    public void MinimumEnforcementEnabled_RequiresDistinctActiveSauceRows()
    {
        var sauces = Rows(2, 0);

        var exception = Assert.Throws<RestaurantSystem.Api.Common.Exceptions.BadRequestException>(() =>
            SauceSelectionRule.EnsureAtLeastMinimum(
                sauces, [sauces[0].Id, sauces[0].Id], ingredientQuantities: null, sauceMin: 2,
                enforcementEnabled: true));

        exception.ErrorCode.Should().Be(ErrorCodes.SauceMinimumNotMet);
        exception.Message.Should().Be(SauceSelectionRule.MinimumNotMetMessage);
    }

    [Fact]
    public void LegacyQuantityMap_CanSatisfyMinimumWhenSelectionListIsAbsent()
    {
        var sauces = Rows(2, 0);
        var quantities = new Dictionary<Guid, int> { [sauces[0].Id] = 1, [sauces[1].Id] = 2 };

        var action = () => SauceSelectionRule.EnsureAtLeastMinimum(
            sauces, selectedIngredientIds: null, quantities, sauceMin: 2, enforcementEnabled: true);

        action.Should().NotThrow();
    }

    private static List<ProductIngredient> Rows(int sauces, int ingredients) =>
        Enumerable.Range(0, sauces + ingredients).Select(index => new ProductIngredient
        {
            Id = Guid.NewGuid(),
            Name = $"row-{index}",
            IsActive = true,
            IsOptional = true,
            Kind = index < sauces ? IngredientKind.Sauce : IngredientKind.Ingredient,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        }).ToList();
}
