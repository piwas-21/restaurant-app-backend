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
                // 2.00 of ingredient customization PLUS the side item below, because
                // BuildRegularItemAsync folds sides into the same field (`customizationPrice +=
                // sideItem.BasePrice * quantity`): 2.00 + 3.50 x 2 = 9.00. Getting this wrong is not
                // cosmetic — a fixture whose CustomizationPrice excludes its own side item describes a
                // row the factory cannot build, and pins a number nothing can produce.
                CustomizationPrice = 9m,
                // A regular row's stored total is (UnitPrice + CustomizationPrice) * Quantity, and it
                // has to be set for the fixture to mean anything: the translator derives the order's
                // customization from it (#312), so an unset ItemTotal describes no real basket row.
                ItemTotal = 19m,
                SelectedSideItems = new List<BasketSideItemDto>
                {
                    new() { Id = sideId, Price = 3.5m, Quantity = 2 }
                }
            }
        };

        var result = _translator.Translate(basketItems);

        var parent = result.Should().ContainSingle().Subject;
        parent.CustomizationPrice.Should().Be(9m, "at quantity 1 the per-unit and line-absolute readings coincide");
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
                // A bundle row's total is UnitPrice * Quantity — its customization is already folded
                // into UnitPrice. Set for the same reason as above (#312).
                ItemTotal = 12.98m,
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
                ItemTotal = 12.98m,
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
                ItemTotal = 10m,
                IngredientQuantities = new Dictionary<Guid, int> { [kept] = 2, [removed] = 1 },
                SelectedIngredients = new List<Guid> { kept },
            },
            // Item with no quantities: the field is omitted (null).
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 5m,
                ItemTotal = 5m,
            }
        };

        var result = _translator.Translate(basketItems);

        var withQuantities = result[0].IngredientQuantities;
        withQuantities.Should().NotBeNull();
        withQuantities![kept].Should().Be(2);
        withQuantities[removed].Should().Be(0, "a deselected ingredient is zeroed for IsRemoved derivation");

        result[1].IngredientQuantities.Should().BeNull();
    }

    // ---- #312: the root line's CustomizationPrice ----------------------------------------------
    //
    // CreateOrderItemDto declares the field as the total for ALL quantities and OrderItemFactory
    // honours that — `(UnitPrice * Quantity) + CustomizationPrice`. The basket stores it two ways, so
    // copying it through broke the contract in opposite directions depending on the row's shape.
    // These are the arithmetic pins; OrderLineCustomizationPriceTests drives the same two shapes
    // through the real endpoint.
    //
    // Both cases are at quantity > 1 deliberately. At quantity 1 the per-unit and line-absolute
    // readings are identical, which is why every test that existed before #312 agreed with the bug.

    [Fact]
    public void RegularRoot_ExpressesTheStoredPerUnitCustomization_AsALineTotal()
    {
        // 12.99 + a 2.99 side item, three of them: the basket stores 2.99 per unit and a line total
        // of (12.99 + 2.99) * 3 = 47.94. Copied through, the order line was 12.99*3 + 2.99 = 41.96.
        var basketItems = new List<BasketItemDto>
        {
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 3,
                UnitPrice = 12.99m,
                CustomizationPrice = 2.99m,
                ItemTotal = 47.94m,
            }
        };

        var root = _translator.Translate(basketItems).Single();

        root.CustomizationPrice.Should().Be(8.97m, "2.99 per unit across three units is 8.97 for the line");
        (root.UnitPrice * root.Quantity + root.CustomizationPrice).Should().Be(47.94m,
            "OrderItemFactory's formula applied to this DTO must reproduce the basket line total");
    }

    [Fact]
    public void BundleRoot_SendsZero_BecauseItsCustomizationIsAlreadyInsideUnitPrice()
    {
        // 16.00 unit price with 3.00 of extras ALREADY folded in (BuildMenuItemAsync's last three
        // statements), two of them: the basket line is 16.00 * 2 = 32.00. Copied through, the order
        // line was 16.00*2 + 3.00 = 35.00 — the extras charged twice.
        //
        // The child row is part of the fixture, not decoration. BasketLineTotal argues that a bundle
        // parent with a non-zero CustomizationPrice and NO children is unmanufacturable — the
        // customization is accumulated only inside the child loop — so a childless fixture here would
        // describe a row the system says cannot exist, and would let a rule that discriminates on the
        // child count pass while being wrong on every real bundle.
        var basketItems = new List<BasketItemDto>
        {
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 2,
                UnitPrice = 16.00m,
                CustomizationPrice = 3.00m,
                ItemTotal = 32.00m,
                ChildItems = new List<BasketItemDto>
                {
                    new()
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 4,          // line-absolute: 2 drinks per bundle x 2 bundles
                        UnitPrice = 1.50m,
                        CustomizationPrice = 1.50m,
                        ItemTotal = 0m,        // children carry zero (#54)
                    }
                }
            }
        };

        var root = _translator.Translate(basketItems).Single();

        root.CustomizationPrice.Should().Be(0m,
            "a bundle's customization is inside UnitPrice; adding it again is the #308 double-charge");
        (root.UnitPrice * root.Quantity + root.CustomizationPrice).Should().Be(32.00m);
        // The guard against the obvious repair: `CustomizationPrice * Quantity` would send 6.00 here
        // and bill 38.00, so a fix that only chases the regular-item case fails on this line.
        root.CustomizationPrice.Should().NotBe(3.00m * 2);
    }

    [Fact]
    public void NegativeCustomization_IsCarriedThrough_NotClamped()
    {
        // Removing a priced included-in-base ingredient is a real deduction (#304), so a basket line
        // can legitimately total less than UnitPrice * Quantity. Clamping at zero would silently
        // re-charge the guest for something they took off.
        var basketItems = new List<BasketItemDto>
        {
            new()
            {
                ProductId = Guid.NewGuid(),
                Quantity = 2,
                UnitPrice = 10.00m,
                CustomizationPrice = -1.00m,
                ItemTotal = 18.00m,
            }
        };

        _translator.Translate(basketItems).Single().CustomizationPrice.Should().Be(-2.00m);
    }
}
