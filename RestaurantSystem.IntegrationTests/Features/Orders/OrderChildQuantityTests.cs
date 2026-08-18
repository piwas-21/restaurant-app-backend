using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Issue #318. Two child rows on the same order meant different things: a top-level SIDE ITEM's
// Quantity is PER UNIT of the parent line, while a bundle option's is already LINE-ABSOLUTE
// (BuildMenuItemAsync stores item.Quantity * option.Quantity, and BundleChildQuantityScaler keeps
// it so — #305). Both render into the same SideItems collection, so an order for 3 pizzas with a
// cola on the side printed "1 cola" while 2 bundles with a cola option printed "2".
//
// The decision was: storage stays per-unit (the basket, the cart and the order agree today and must
// keep agreeing), the RENDERER reconciles them, and the discriminator is persisted at write time
// instead of being derived from the parent's mutable Product.Type.
//
// EVERY ASSERTION HERE IS ON A CHILD ROW, never on a total, and that is not a stylistic choice:
// a child carries ItemTotal = 0 (#54) and the root row equals its basket line (#312), so every
// total in the system reconciles with this defect fully present. A totals-based test cannot see it.
[Collection("Database Lane 4")]
public class OrderChildQuantityTests : IntegrationTestBase
{
    public OrderChildQuantityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- the renderer ---------------------------------------------------------------------------

    // The defect itself: 3 pizzas, 1 cola on the side, stored per-unit, must print 3.
    [Fact]
    public void ASideChild_IsScaledByTheParentLineQuantity()
    {
        var dto = Map(ParentWithChild(ProductType.MainItem, parentQuantity: 3, childQuantity: 1,
            childKind: OrderItemKind.SideItem));

        dto.SideItems!.Single().Quantity.Should().Be(3);
    }

    // THE GUARD AGAINST THE OBVIOUS WRONG FIX. A bundle child is already line-absolute, so scaling
    // it here would give quantity-squared behaviour: this row was written as 3 x 2 = 6 for a line of
    // 3, and it must still read 6, not 18.
    [Fact]
    public void ABundleChild_IsNotScaledAgain()
    {
        var dto = Map(ParentWithChild(ProductType.Menu, parentQuantity: 3, childQuantity: 6,
            childKind: OrderItemKind.BundleChild));

        dto.SideItems!.Single().Quantity.Should().Be(6);
    }

    // #318 item 3. Product.Type is MUTABLE — UpdateProductCommand assigns it unguarded — so deriving
    // the kind from the parent's CURRENT type relabelled the children of orders already placed. A
    // row that recorded what it is must win over what its parent's product happens to be today.
    [Fact]
    public void APersistedKind_BeatsTheParentsCurrentProductType()
    {
        var dto = Map(ParentWithChild(ProductType.Menu, parentQuantity: 3, childQuantity: 1,
            childKind: OrderItemKind.SideItem));

        dto.SideItems!.Single().Kind.Should().Be(OrderItemKind.SideItem,
            "the row's own provenance outranks the parent product's current type");
        dto.SideItems!.Single().Quantity.Should().Be(3,
            "and the quantity rule must follow the row's kind, not the parent's type");
    }

    // Every order placed before the Kind column existed has null there, and the old derivation is
    // exactly what produced their LABEL at the time — so the label must not move. The QUANTITY does
    // move for a historical side child (4 under a line of 2 now reads 8), and that is the fix
    // applying to the orders that have the defect, not a regression.
    [Theory]
    [InlineData(ProductType.Menu, OrderItemKind.BundleChild, 4)]
    [InlineData(ProductType.MainItem, OrderItemKind.SideItem, 8)]
    public void AHistoricalRowWithNoKind_FallsBackToTheParentProductType(
        ProductType parentType, OrderItemKind expectedKind, int expectedQuantity)
    {
        var dto = Map(ParentWithChild(parentType, parentQuantity: 2, childQuantity: 4, childKind: null));

        dto.SideItems!.Single().Kind.Should().Be(expectedKind);
        dto.SideItems!.Single().Quantity.Should().Be(expectedQuantity);
    }

    // THE REGRESSION AN ADVERSARIAL REVIEW MEASURED, and the reason the scaling test is not simply
    // "DisplayKind == SideItem". `parent.Product?.Type == ProductType.Menu` is false BOTH for a plain
    // product AND when the navigation is null — and it really does go null, because the global
    // IsDeleted query filter applies to Include, so soft-deleting a bundle product empties it on
    // every historical order that referenced it. That was harmless while the answer only picked a
    // label. Feeding it to a multiplier turned a stored 6 into 18 on a 3-unit line — the exact
    // quantity-squared outcome this fix exists to avoid — on every pre-existing order.
    //
    // The label is deliberately left as it always was; only the quantity is held back.
    [Fact]
    public void AChildWhoseParentProductCannotBeResolved_IsNotScaled()
    {
        var parent = ParentWithChild(ProductType.Menu, parentQuantity: 3, childQuantity: 6, childKind: null);
        parent.Product = null; // what a soft-deleted bundle product leaves behind

        var dto = Map(parent);

        dto.SideItems!.Single().Quantity.Should().Be(6, "an unclassifiable row must be left alone, not multiplied");
        dto.SideItems!.Single().Kind.Should().Be(OrderItemKind.SideItem, "the label is unchanged from before this fix");
    }

    // Mirrors #305's rule: skip a row it cannot reason about rather than invent a number. A line
    // quantity is bounded 1..100 but a stored side quantity is bounded nowhere, so the product can
    // leave an int.
    [Fact]
    public void ASideQuantityThatWouldOverflow_IsLeftUnscaled()
    {
        var dto = Map(ParentWithChild(ProductType.MainItem, parentQuantity: 100,
            childQuantity: int.MaxValue, childKind: OrderItemKind.SideItem));

        dto.SideItems!.Single().Quantity.Should().Be(int.MaxValue);
    }

    // The root line is neither kind, and nothing about this change may start scaling it.
    [Fact]
    public void TheRootLineKeepsItsOwnQuantityAndNoKind()
    {
        var dto = Map(ParentWithChild(ProductType.MainItem, parentQuantity: 3, childQuantity: 1,
            childKind: OrderItemKind.SideItem));

        dto.Kind.Should().BeNull();
        dto.Quantity.Should().Be(3);
    }

    // THE PATH PRODUCTION ACTUALLY USES. Every test above goes through MapToOrderItemDto, which
    // OrderMappingService itself warns against ("Prefer MapToOrderDto") — #234 was the incident where
    // that divergence bit, because real reads (printer feed, order list, focus orders) source children
    // from the order's flat Items list keyed on ParentOrderItemId, not from the ChildOrderItems
    // navigation. A rule pinned only on the entry point nothing calls is not pinned.
    [Fact]
    public void TheOrderLevelMapping_AppliesTheSameRule_ViaTheParentIdLookup()
    {
        var order = OrderWithChildren(parentQuantity: 3, sideQuantity: 1, bundleChildQuantity: 6);

        using var scope = Factory.Services.CreateScope();
        var dto = scope.ServiceProvider.GetRequiredService<IOrderMappingService>().MapToOrderDto(order);

        var side = dto.Items.Single(i => i.ProductName == "Side").SideItems!.Single();
        var bundle = dto.Items.Single(i => i.ProductName == "Bundle").SideItems!.Single();

        side.Quantity.Should().Be(3, "a side child is per unit of its line");
        bundle.Quantity.Should().Be(6, "a bundle child is already line-absolute");
    }

    // ---- the producer ---------------------------------------------------------------------------

    // The renderer above is only correct if the rows actually carry a kind, and only the translator
    // can put one there. Driving the real translator rather than hand-building the DTOs, because the
    // claim under test is "the producer stamps both kinds", which a hand-built DTO would assume.
    [Fact]
    public void TheTranslator_StampsBothChildKinds()
    {
        var translated = new BasketToOrderTranslator().Translate(
        [
            new BasketItemDto
            {
                ProductId = Guid.NewGuid(),
                Quantity = 3,
                UnitPrice = 10m,
                ItemTotal = 30m,
                SelectedSideItems =
                [
                    new BasketSideItemDto { Id = Guid.NewGuid(), Quantity = 1, Price = 2m }
                ]
            },
            new BasketItemDto
            {
                ProductId = Guid.NewGuid(),
                Quantity = 2,
                UnitPrice = 16m,
                ItemTotal = 32m,
                ChildItems =
                [
                    new BasketItemDto { ProductId = Guid.NewGuid(), Quantity = 4, UnitPrice = 0m }
                ]
            }
        ]);

        translated[0].ChildItems!.Single().Kind.Should().Be(OrderItemKind.SideItem);
        translated[1].ChildItems!.Single().Kind.Should().Be(OrderItemKind.BundleChild);

        // Storage is deliberately untouched: the side item keeps the per-unit 1 the client sent, and
        // the bundle child keeps the line-absolute 4 the basket stored. Scaling either here would
        // make the order disagree with the basket the guest saw, which is the fix #318 rejected.
        translated[0].ChildItems!.Single().Quantity.Should().Be(1);
        translated[1].ChildItems!.Single().Quantity.Should().Be(4);
    }

    // The wire contract, pinned because moving this enum between assemblies is exactly the change
    // that could break it silently — and because the first version of its doc comment got the
    // direction backwards. Two different representations are in play: EF stores an INTEGER (so the
    // member ORDER is what the database sees), while the API registers a StringEnumConverterFactory
    // whose factory explicitly handles Nullable<TEnum>, so the wire carries the NAME. The frontend
    // filters child rows with `c.kind === 'SideItem'` (lineSummary.ts), a string comparison, so a
    // rename here would silently reclassify every bundle component as a side item in the cart.
    [Theory]
    [InlineData(OrderItemKind.SideItem, "SideItem")]
    [InlineData(OrderItemKind.BundleChild, "BundleChild")]
    public void TheKindIsSerialisedByNameForTheWire(OrderItemKind kind, string expected)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new StringEnumConverterFactory());

        var json = JsonSerializer.Serialize(new OrderItemDto { ProductName = "Cola", Kind = kind }, options);

        json.Should().Contain($"\"kind\":\"{expected}\"");
    }

    private OrderItemDto Map(OrderItem parent)
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOrderMappingService>().MapToOrderItemDto(parent);
    }

    /// <summary>
    /// An order whose children hang off <c>ParentOrderItemId</c> in the flat <c>Items</c> list —
    /// the shape <c>MapToOrderDto</c> builds its lookup from, and the shape real queries return.
    /// </summary>
    private static Order OrderWithChildren(int parentQuantity, int sideQuantity, int bundleChildQuantity)
    {
        var sideParent = ParentWithChild(ProductType.MainItem, parentQuantity, sideQuantity, OrderItemKind.SideItem);
        sideParent.ProductName = "Side";
        var bundleParent = ParentWithChild(ProductType.Menu, parentQuantity, bundleChildQuantity, OrderItemKind.BundleChild);
        bundleParent.ProductName = "Bundle";

        var items = new List<OrderItem>();
        foreach (var parent in new[] { sideParent, bundleParent })
        {
            var child = parent.ChildOrderItems.Single();
            child.ParentOrderItemId = parent.Id;
            // Cleared so the lookup is the ONLY way children can be found — otherwise this test
            // could pass through the navigation and prove nothing about the production path.
            parent.ChildOrderItems = [];
            items.Add(parent);
            items.Add(child);
        }

        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "T-1",
            Items = items,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
    }

    private static OrderItem ParentWithChild(
        ProductType parentType, int parentQuantity, int childQuantity, OrderItemKind? childKind) => new()
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductName = "Parent",
            Quantity = parentQuantity,
            UnitPrice = 10m,
            ItemTotal = 10m * parentQuantity,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            Product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Parent",
                Type = parentType,
                Ingredients = [],
                Allergens = [],
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            },
            ChildOrderItems =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ProductName = "Cola",
                    Quantity = childQuantity,
                    UnitPrice = 2m,
                    ItemTotal = 0m,
                    Kind = childKind,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "test"
                }
            ]
        };
}
