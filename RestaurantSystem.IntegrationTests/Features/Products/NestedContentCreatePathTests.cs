using FluentAssertions;
using FluentValidation.Results;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// #316's CREATE half. The endpoint suite (NestedContentValidationTests) drives PUT only, so the
// create validator was WIRED but unexercised — and "the shared rule exists" and "this validator
// applies it" are different claims. That distinction is not theoretical here: ProductContentRuleTests
// exists for exactly the same reason, after #192/#193 had to delete one duplicated check from four
// handlers separately, and an adversarial review of this change found a create-path regression that
// no test could see.
//
// Validator-level rather than through POST: the rule is the thing under test, and driving the real
// endpoint would need a full valid product payload whose unrelated rules would have to be satisfied
// and maintained. The endpoint suite already proves the pipeline runs these validators.
public class NestedContentCreatePathTests
{
    private static ValidationResult Validate(
        List<CreateProductVariationDto>? variations = null,
        List<ProductIngredientDto>? ingredients = null)
    {
        var categoryId = Guid.NewGuid();
        return new CreateProductCommandValidator().Validate(new CreateProductCommand(
            Name: "p", Description: null, BasePrice: 1m, IsActive: true, IsAvailable: true,
            IsSpecial: false, PreparationTimeMinutes: 1, Type: ProductType.MainItem,
            KitchenType: KitchenType.None, Ingredients: null, Allergens: null, DisplayOrder: 0,
            CategoryIds: [categoryId], PrimaryCategoryId: categoryId,
            Variations: variations, SuggestedSideItemIds: null, DetailedIngredients: ingredients,
            Content: new ProductDescriptionsDto { ["en"] = new() { Name = "n", Description = "d" } }));
    }

    private static CreateProductVariationDto Variation(Dictionary<string, ProductVariationContentDto>? content) =>
        new("Large", null, 1m, true, 1, content);

    private static ProductIngredientDto Ingredient(Dictionary<string, ProductIngredientContentDto>? content) =>
        new() { Name = "Cheese", IsActive = true, Content = content };

    private static List<string> Messages(ValidationResult result) =>
        result.Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void CreateValidator_RejectsANullIngredientEntry()
    {
        var result = Validate(ingredients: [Ingredient(new() { ["en"] = null! })]);

        Messages(result).Should().Contain(m => m.StartsWith(NestedContentRule.EntryRequiredMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void CreateValidator_RejectsANullVariationEntry()
    {
        var result = Validate(variations: [Variation(new() { ["en"] = null! })]);

        Messages(result).Should().Contain(m => m.StartsWith(NestedContentRule.EntryRequiredMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void CreateValidator_RejectsABlankLanguageKey()
    {
        var result = Validate(ingredients: [Ingredient(new() { ["  "] = new() { Name = "n" } })]);

        Messages(result).Should().Contain(NestedContentRule.LanguageKeyRequiredMessage);
    }

    [Fact]
    public void CreateValidator_RejectsANullIngredientName()
    {
        var result = Validate(ingredients: [Ingredient(new() { ["en"] = new() { Name = null!, Description = "d" } })]);

        Messages(result).Should().Contain(m => m.StartsWith(NestedContentRule.NameRequiredMessage, StringComparison.Ordinal));
    }

    // The bound an earlier version of this fix documented as absent, on the path that had no tests.
    [Fact]
    public void CreateValidator_RejectsAVariationNameOverItsColumn()
    {
        var result = Validate(variations: [Variation(new() { ["en"] = new() { Name = new string('x', 101) } })]);

        Messages(result).Should().Contain(
            NestedContentRule.NameTooLongMessage("en", NestedContentRule.VariationNameMaxLength));
    }

    [Fact]
    public void CreateValidator_RejectsADescriptionOverItsColumn()
    {
        var result = Validate(ingredients:
            [Ingredient(new() { ["en"] = new() { Name = "n", Description = new string('x', 501) } })]);

        Messages(result).Should().Contain(NestedContentRule.DescriptionTooLongMessage("en"));
    }

    // The over-strictness direction: an ordinary admin payload, and the absent-content case, must both
    // pass. Asserted as "no NESTED content error" rather than IsValid, because this skeleton command
    // trips unrelated rules that have nothing to do with this one.
    [Fact]
    public void CreateValidator_AcceptsWellFormedAndAbsentNestedContent()
    {
        var result = Validate(
            variations: [Variation(new() { ["en"] = new() { Name = "Large", Description = null } }), Variation(null)],
            ingredients: [Ingredient(new() { ["en"] = new() { Name = "Cheese" } }), Ingredient(null)]);

        Messages(result).Should().NotContain(m =>
            m.StartsWith(NestedContentRule.EntryRequiredMessage, StringComparison.Ordinal)
            || m.StartsWith(NestedContentRule.NameRequiredMessage, StringComparison.Ordinal)
            || m == NestedContentRule.LanguageKeyRequiredMessage);
    }
}
