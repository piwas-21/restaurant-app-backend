using FluentAssertions;
using RestaurantSystem.Api.Features.Basket.Commands.AddToBasketCommand;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// Issue #150 review follow-up. Bundle-child SpecialInstructions are persisted
/// on child BasketItem rows (varchar(500)) since #150, but only the parent's
/// SpecialInstructions was validated — an oversized child value would surface
/// as a DbUpdateException (HTTP 500) instead of a clean validation 400.
/// </summary>
public class AddToBasketCommandValidatorTests
{
    private readonly AddToBasketCommandValidator _validator = new();

    private static AddToBasketCommand BuildCommand(
        string? specialInstructions = null,
        List<SelectedMenuOptionDto>? selectedMenuOptions = null) => new(
            SessionId: "session-1",
            ProductId: Guid.NewGuid(),
            ProductVariationId: null,
            MenuId: null,
            Quantity: 1,
            SpecialInstructions: specialInstructions,
            SelectedIngredients: null,
            ExcludedIngredients: null,
            AddedIngredients: null,
            IngredientQuantities: null,
            SelectedSideItems: null,
            SelectedMenuOptions: selectedMenuOptions);

    [Fact]
    public void Validate_ChildSpecialInstructionsOver500Chars_Fails()
    {
        var command = BuildCommand(selectedMenuOptions: new List<SelectedMenuOptionDto>
        {
            new()
            {
                SectionId = Guid.NewGuid(),
                ItemId = Guid.NewGuid(),
                Quantity = 1,
                SpecialInstructions = new string('x', 501)
            }
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "SelectedMenuOptions[0].SpecialInstructions" &&
            e.ErrorMessage == "Special instructions cannot exceed 500 characters");
    }

    [Fact]
    public void Validate_ChildSpecialInstructionsAtLimitOrNull_Passes()
    {
        var command = BuildCommand(selectedMenuOptions: new List<SelectedMenuOptionDto>
        {
            new()
            {
                SectionId = Guid.NewGuid(),
                ItemId = Guid.NewGuid(),
                Quantity = 1,
                SpecialInstructions = new string('x', 500)
            },
            new()
            {
                SectionId = Guid.NewGuid(),
                ItemId = Guid.NewGuid(),
                Quantity = 1,
                SpecialInstructions = null
            }
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ParentSpecialInstructionsOver500Chars_StillFails()
    {
        var command = BuildCommand(specialInstructions: new string('x', 501));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "SpecialInstructions" &&
            e.ErrorMessage == "Special instructions cannot exceed 500 characters");
    }
}
