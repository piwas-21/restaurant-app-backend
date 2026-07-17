using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Domain.Entities;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #155 (slice 3): LineCustomizationBuilder is the single writer of a line's ingredient
// customization for both the regular and bundle-child paths. These pin the two (historically
// divergent, behaviour-preserved) IngredientQuantitiesJson precedence modes it exposes via the
// preferProvidedQuantities flag; the end-to-end price/backfill behaviour is additionally covered
// by the BasketDedup / BundleChild integration tests. Pure unit test — no DB.
public class LineCustomizationBuilderTests
{
    private static readonly Guid Cheese = Guid.NewGuid(); // optional, deselected in the tests
    private static readonly Guid Bacon = Guid.NewGuid(); // optional, selected in the tests

    private static LineCustomizationBuilder Build(decimal price = 0m)
    {
        var pricing = new Mock<IBasketPricingService>();
        pricing
            .Setup(p => p.CalculateIngredientCustomizationPrice(
                It.IsAny<IEnumerable<ProductIngredient>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyDictionary<Guid, int>>()))
            .Returns(price);
        return new LineCustomizationBuilder(pricing.Object);
    }

    private static List<ProductIngredient> Ingredients() =>
    [
        new() { Id = Cheese, Name = "Cheese", IsOptional = true, IsActive = true, MaxQuantity = 3, ProductId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, CreatedBy = "test" },
        new() { Id = Bacon, Name = "Bacon", IsOptional = true, IsActive = true, MaxQuantity = 3, ProductId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, CreatedBy = "test" },
    ];

    private static Dictionary<Guid, int>? Deserialize(string? json) =>
        json == null ? null : JsonSerializer.Deserialize<Dictionary<Guid, int>>(json);

    [Fact]
    public void PreferProvided_WithExplicitMap_PersistsVerbatim_AndDelegatesPrice()
    {
        var provided = new Dictionary<Guid, int> { [Bacon] = 2 };

        var result = Build(price: 3m).Build(Ingredients(), [Bacon], null, provided, preferProvidedQuantities: true);

        result.CustomizationPrice.Should().Be(3m);
        // Verbatim: only what the client sent — no backfilled 0 for the deselected cheese.
        Deserialize(result.IngredientQuantitiesJson).Should().BeEquivalentTo(provided);
    }

    [Fact]
    public void PreferProvided_WithoutMap_BackfillsFromSelection()
    {
        var result = Build().Build(Ingredients(), [Bacon], null, null, preferProvidedQuantities: true);

        // Backfill: selected bacon = 1, deselected cheese = 0.
        Deserialize(result.IngredientQuantitiesJson)
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [Bacon] = 1, [Cheese] = 0 });
    }

    [Fact]
    public void BundleChild_WithSelection_BackfillsEvenWhenQuantitiesProvided()
    {
        var provided = new Dictionary<Guid, int> { [Bacon] = 2 };

        var result = Build().Build(Ingredients(), [Bacon], null, provided, preferProvidedQuantities: false);

        // Backfill wins, but honours the provided bacon quantity; the deselected cheese gets 0.
        Deserialize(result.IngredientQuantitiesJson)
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [Bacon] = 2, [Cheese] = 0 });
    }

    [Fact]
    public void BundleChild_WithoutSelection_PersistsProvidedMap()
    {
        var provided = new Dictionary<Guid, int> { [Bacon] = 2 };

        var result = Build().Build(Ingredients(), null, null, provided, preferProvidedQuantities: false);

        Deserialize(result.IngredientQuantitiesJson).Should().BeEquivalentTo(provided);
    }
}
