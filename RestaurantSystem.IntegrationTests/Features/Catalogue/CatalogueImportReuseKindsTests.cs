using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueImportReuseKindsTests
{
    [Theory]
    [InlineData("ingredient", IngredientKind.Ingredient, true)]
    [InlineData("ingredient", IngredientKind.Sauce, false)]
    [InlineData("sauce", IngredientKind.Ingredient, false)]
    [InlineData("sauce", IngredientKind.Sauce, true)]
    [InlineData("unknown", IngredientKind.Sauce, false)]
    public void IngredientReuse_requires_matching_role(string role, IngredientKind kind, bool expected)
    {
        using var document = JsonDocument.Parse($$"""{"role":"{{role}}"}""");
        CatalogueImportReuseKinds.MatchesIngredient(document.RootElement, kind).Should().Be(expected);
    }

    [Theory]
    [InlineData("ingredient", OptionSetKind.Ingredient, true)]
    [InlineData("ingredient", OptionSetKind.Sauce, false)]
    [InlineData("sauce", OptionSetKind.Sauce, true)]
    [InlineData("bundle-option", OptionSetKind.BundleChoice, true)]
    [InlineData("bundle-option", OptionSetKind.SuggestedSide, false)]
    [InlineData("suggested-side", OptionSetKind.SuggestedSide, true)]
    [InlineData("unknown", OptionSetKind.BundleChoice, false)]
    public void OptionSetReuse_requires_matching_kind(string kindName, OptionSetKind kind, bool expected)
    {
        using var document = JsonDocument.Parse($$"""{"kind":"{{kindName}}"}""");
        CatalogueImportReuseKinds.MatchesOptionSet(document.RootElement, kind).Should().Be(expected);
    }
}
