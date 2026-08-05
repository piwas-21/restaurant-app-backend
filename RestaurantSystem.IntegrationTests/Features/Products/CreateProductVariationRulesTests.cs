using FluentAssertions;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// #306 moved the per-variation rules out of CreateProductCommandValidator into
// CreateProductVariationRules, to keep that validator under the CLAUDE.md §4 60-line limit without
// banking it into the file-length baseline.
//
// The move had NO coverage — `grep "Variation name is required"` over the test project returned
// nothing before this file — so "a pure extraction, behaviour unchanged" was a claim rather than a
// fact, and a `ChildRules(...)` that silently stopped being registered would have looked identical.
// These three assertions are what make it checkable: they fail if the extracted rules are not
// reached through the real validator.
public class CreateProductVariationRulesTests
{
    private static readonly CreateProductCommandValidator Validator = new();

    private static List<string> ValidateVariation(CreateProductVariationDto variation)
    {
        var categoryId = Guid.NewGuid();
        var command = new CreateProductCommand(
            Name: "p", Description: null, BasePrice: 1m, IsActive: true, IsAvailable: true,
            IsSpecial: false, PreparationTimeMinutes: 1, Type: ProductType.MainItem,
            KitchenType: KitchenType.None, Ingredients: null, Allergens: null, DisplayOrder: 0,
            CategoryIds: new List<Guid> { categoryId }, PrimaryCategoryId: categoryId,
            Variations: new List<CreateProductVariationDto> { variation },
            SuggestedSideItemIds: null, DetailedIngredients: null,
            Content: new ProductDescriptionsDto
            {
                ["en"] = new ProductDescriptionDto { Name = "n", Description = "d" }
            });

        return Validator.Validate(command).Errors.Select(e => e.ErrorMessage).ToList();
    }

    private static CreateProductVariationDto Variation(
        string name = "Large", string? description = null, int displayOrder = 0) =>
        new(Name: name, Description: description, PriceModifier: 1m, IsActive: true,
            DisplayOrder: displayOrder, Content: null);

    [Fact]
    public void BlankVariationName_IsRejected() =>
        ValidateVariation(Variation(name: "")).Should().Contain("Variation name is required");

    [Fact]
    public void OverlongVariationName_IsRejected() =>
        ValidateVariation(Variation(name: new string('x', 51)))
            .Should().Contain("Variation name cannot exceed 50 characters");

    [Fact]
    public void OverlongVariationDescription_IsRejected() =>
        ValidateVariation(Variation(description: new string('x', 201)))
            .Should().Contain("Variation description cannot exceed 200 characters");

    [Fact]
    public void NegativeVariationDisplayOrder_IsRejected() =>
        ValidateVariation(Variation(displayOrder: -1))
            .Should().Contain("Variation display order cannot be negative");

    [Fact]
    public void AWellFormedVariation_RaisesNoVariationError() =>
        ValidateVariation(Variation(description: "fine"))
            .Should().NotContain(e => e.StartsWith("Variation ", StringComparison.Ordinal));
}
