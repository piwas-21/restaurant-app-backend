using FluentAssertions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Slice 5 (#157): pure unit tests for the server-side basket→order translation (ported from the
// former frontend orderItemsPayload.ts). No DB — asserts the mapping edges the integration parity
// test's fixture doesn't exercise: top-level side items, bundle-child customization-price zeroing,
// and deselected-ingredient zeroing.
public class BasketToOrderTranslatorTests
{
    private readonly BasketToOrderTranslator _translator = new();

    [Fact]
    public void MapsTopLevelSideItems_AsChildRows_WithZeroCustomizationPrice()
    {
        var sideId = Guid.NewGuid();
        var basketItems = new List<BasketItemDto>
        {
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 10m,
                CustomizationPrice = 2m,
                SelectedSideItems = new List<BasketSideItemDto>
                {
                    new() { Id = sideId, Price = 3.5m, Quantity = 2 }
                }
            }
        };

        var result = _translator.Translate(basketItems);

        var parent = result.Should().ContainSingle().Subject;
        parent.CustomizationPrice.Should().Be(2m);
        parent.ChildItems.Should().ContainSingle();
        var side = parent.ChildItems!.Single();
        side.ProductId.Should().Be(sideId);
        side.Quantity.Should().Be(2);
        side.UnitPrice.Should().Be(3.5m);
        side.CustomizationPrice.Should().Be(0m, "a side's price is not a rolled-up customization");
    }

    [Fact]
    public void MapsBundleChild_ForcesZeroCustomizationPrice_AndKeepsInstructions()
    {
        var childProductId = Guid.NewGuid();
        var basketItems = new List<BasketItemDto>
        {
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 12.98m,
                ChildItems = new List<BasketItemDto>
                {
                    new()
                    {
                        ProductId = childProductId,
                        Quantity = 1,
                        UnitPrice = 1.99m,
                        // A non-zero child customization on the basket must NOT propagate — it is
                        // already rolled into the parent UnitPrice (issue #150).
                        CustomizationPrice = 5m,
                        SpecialInstructions = "No ice",
                    }
                }
            }
        };

        var child = _translator.Translate(basketItems).Single().ChildItems!.Single();

        child.ProductId.Should().Be(childProductId);
        child.UnitPrice.Should().Be(1.99m);
        child.CustomizationPrice.Should().Be(0m);
        child.SpecialInstructions.Should().Be("No ice");
    }

    // Issue #150 — the last untested link in the bundle-child customization chain.
    //
    // #150's coverage was written against the FRONTEND util that built childItems with explicit-0
    // removals; slice 5 deleted that util and moved the job here, so the producer those tests
    // pinned no longer exists. What was left pinned the zeroing on a TOP-LEVEL item only
    // (BuildIngredientQuantities_ZeroesDeselected_AndOmitsWhenEmpty) and pinned a bundle child's
    // price and instructions but never its IngredientQuantities
    // (MapsBundleChild_ForcesZeroCustomizationPrice_AndKeepsInstructions). Neither holds the
    // combination the issue is actually about.
    //
    // An explicit 0 is the whole mechanism: OrderMappingService derives IsRemoved from quantity == 0,
    // which is what makes the kitchen ticket print "NO Cheese" for a bundle child. Dropping the key
    // instead of zeroing it reads as "not customized" and the removal never reaches the kitchen.
    [Fact]
    public void MapsBundleChild_ZeroesItsOwnDeselectedIngredients()
    {
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var basketItems = new List<BasketItemDto>
        {
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 12.98m,
                ChildItems = new List<BasketItemDto>
                {
                    new()
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1,
                        UnitPrice = 1.99m,
                        IngredientQuantities = new Dictionary<Guid, int> { [kept] = 2, [removed] = 1 },
                        SelectedIngredients = new List<Guid> { kept },
                    }
                }
            }
        };

        var child = _translator.Translate(basketItems).Single().ChildItems!.Single();

        child.IngredientQuantities.Should().NotBeNull("a child's customizations must survive the hop");
        child.IngredientQuantities![kept].Should().Be(2);
        child.IngredientQuantities[removed].Should().Be(
            0, "a deselected ingredient on a CHILD is zeroed, not dropped — that 0 is what OrderMappingService turns into IsRemoved");
    }

    [Fact]
    public void BuildIngredientQuantities_ZeroesDeselected_AndOmitsWhenEmpty()
    {
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var basketItems = new List<BasketItemDto>
        {
            // Item with quantities: the deselected ingredient is zeroed, not dropped.
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 10m,
                IngredientQuantities = new Dictionary<Guid, int> { [kept] = 2, [removed] = 1 },
                SelectedIngredients = new List<Guid> { kept },
            },
            // Item with no quantities: the field is omitted (null).
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 5m,
            }
        };

        var result = _translator.Translate(basketItems);

        var withQuantities = result[0].IngredientQuantities;
        withQuantities.Should().NotBeNull();
        withQuantities![kept].Should().Be(2);
        withQuantities[removed].Should().Be(0, "a deselected ingredient is zeroed for IsRemoved derivation");

        result[1].IngredientQuantities.Should().BeNull();
    }
}
