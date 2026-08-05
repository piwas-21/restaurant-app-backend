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

    private static LineCustomizationBuilder Build(decimal price = 0m) => Build(price, out _);

    private static LineCustomizationBuilder Build(decimal price, out Mock<IBasketPricingService> pricing)
    {
        pricing = new Mock<IBasketPricingService>();
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

        var result = Build(price: 3m).Build(Ingredients(), [Bacon], provided, preferProvidedQuantities: true);

        result.CustomizationPrice.Should().Be(3m);
        // Verbatim: only what the client sent — no backfilled 0 for the deselected cheese.
        Deserialize(result.IngredientQuantitiesJson).Should().BeEquivalentTo(provided);
    }

    [Fact]
    public void PreferProvided_WithoutMap_BackfillsFromSelection()
    {
        var result = Build().Build(Ingredients(), [Bacon], null, preferProvidedQuantities: true);

        // Backfill: selected bacon = 1, deselected cheese = 0.
        Deserialize(result.IngredientQuantitiesJson)
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [Bacon] = 1, [Cheese] = 0 });
    }

    // The symmetric twin of BundleChild_WithoutSelection_PersistsProvidedMap, and the unit-level
    // pin for #303: with neither a selection nor a map there is nothing to record, and the branch
    // must NOT invent a backfill. It used to, writing a 0 for every unselected active
    // optional-or-included ingredient of a re-ordered line — which is what Cheese and Bacon are in
    // this fixture, so reverting the guard really does fail this test. See ReorderKitchenTicketTests
    // for what those zeroes did to the kitchen ticket once they reached the order.
    [Fact]
    public void PreferProvided_WithoutSelectionOrMap_WritesNothing()
    {
        var result = Build().Build(Ingredients(), null, null, preferProvidedQuantities: true);

        result.IngredientQuantitiesJson.Should().BeNull();
    }

    // The price half of #303, and the half that costs money. The pricing service reads a null
    // selection as "everything deselected" and deducts every included-in-base ingredient — a real
    // discount on a dish nobody changed. Asserted as "never asked" rather than "returned 0" so a
    // mocked 0 cannot make it look right: the question itself must not be put.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithoutSelectionOrMap_DoesNotPriceACustomization(bool preferProvidedQuantities)
    {
        var builder = Build(price: -1.00m, out var pricing);

        var result = builder.Build(Ingredients(), null, null, preferProvidedQuantities);

        result.CustomizationPrice.Should().Be(0m);
        pricing.Verify(
            p => p.CalculateIngredientCustomizationPrice(
                It.IsAny<IEnumerable<ProductIngredient>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyDictionary<Guid, int>>()),
            Times.Never);
    }

    // …but a payload that DID express a choice is still priced by the service, on both paths. The
    // gate is about the absence of an answer, not about the selection specifically — an explicit
    // quantity map with no selection list is an answer too.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithAQuantityMapAlone_StillPricesACustomization(bool preferProvidedQuantities)
    {
        var builder = Build(price: 2.50m, out var pricing);

        var result = builder.Build(
            Ingredients(), null, new Dictionary<Guid, int> { [Bacon] = 2 }, preferProvidedQuantities);

        result.CustomizationPrice.Should().Be(2.50m);
        pricing.Verify(
            p => p.CalculateIngredientCustomizationPrice(
                It.IsAny<IEnumerable<ProductIngredient>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyDictionary<Guid, int>>()),
            Times.Once);
    }

    // An EMPTY selection is still a selection — the guest opened the sheet and took everything off.
    // The gate tests for null, not for emptiness, and this is what keeps that deliberate.
    [Fact]
    public void PreferProvided_WithEmptySelection_StillBackfills()
    {
        var result = Build().Build(Ingredients(), [], null, preferProvidedQuantities: true);

        Deserialize(result.IngredientQuantitiesJson)
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [Bacon] = 0, [Cheese] = 0 });
    }

    [Fact]
    public void BundleChild_WithSelection_BackfillsEvenWhenQuantitiesProvided()
    {
        var provided = new Dictionary<Guid, int> { [Bacon] = 2 };

        var result = Build().Build(Ingredients(), [Bacon], provided, preferProvidedQuantities: false);

        // Backfill wins, but honours the provided bacon quantity; the deselected cheese gets 0.
        Deserialize(result.IngredientQuantitiesJson)
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [Bacon] = 2, [Cheese] = 0 });
    }

    [Fact]
    public void BundleChild_WithoutSelection_PersistsProvidedMap()
    {
        var provided = new Dictionary<Guid, int> { [Bacon] = 2 };

        var result = Build().Build(Ingredients(), null, provided, preferProvidedQuantities: false);

        Deserialize(result.IngredientQuantitiesJson).Should().BeEquivalentTo(provided);
    }
}
