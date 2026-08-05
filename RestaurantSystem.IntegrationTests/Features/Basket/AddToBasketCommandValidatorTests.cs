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

    private static AddToBasketCommand BuildCommandWithOptionQuantity(int optionQuantity) =>
        BuildCommand(selectedMenuOptions: new List<SelectedMenuOptionDto>
        {
            new() { SectionId = Guid.NewGuid(), ItemId = Guid.NewGuid(), Quantity = optionQuantity }
        });

    // Issue #308 item 3. The 1..100 rule binds the LINE quantity; an option's own quantity had no
    // upper bound anywhere, and BasketItemFactory rejected only < 1. 30,000,000 is the value that
    // was measured being accepted with a 200 — a child row is stored line-absolute, so it then flowed
    // into the arithmetic and into the decimal price columns, and a later quantity change answered
    // 500 on a Postgres numeric overflow. int.MaxValue is included because it is the boundary the
    // widened 64-bit rescale in BundleChildQuantityScaler was left carrying on its own.
    [Theory]
    [InlineData(101)]
    [InlineData(30_000_000)]
    [InlineData(int.MaxValue)]
    public void Validate_OptionQuantityAboveTheLineBound_Fails(int optionQuantity)
    {
        var result = _validator.Validate(BuildCommandWithOptionQuantity(optionQuantity));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "SelectedMenuOptions[0].Quantity" &&
            e.ErrorMessage == "Menu option quantity cannot exceed 100");
    }

    // The lower bound was the only one that existed, and it lived in the factory rather than the
    // validator — so a 0 became a BadRequestException deep inside the build instead of a validation
    // 400. Both are 400s to a client; pinning it here is what says the rule now has one home.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_OptionQuantityBelowOne_Fails(int optionQuantity)
    {
        var result = _validator.Validate(BuildCommandWithOptionQuantity(optionQuantity));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "SelectedMenuOptions[0].Quantity" &&
            e.ErrorMessage == "Menu option quantity must be greater than 0");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validate_OptionQuantityWithinTheBound_Passes(int optionQuantity)
    {
        _validator.Validate(BuildCommandWithOptionQuantity(optionQuantity)).IsValid.Should().BeTrue();
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
