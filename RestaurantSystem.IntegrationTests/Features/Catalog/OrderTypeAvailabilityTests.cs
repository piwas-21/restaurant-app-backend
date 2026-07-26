using FluentAssertions;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

// OrderTypeAvailability is the single resolver behind the `availability` field on every catalog
// projection. These pin the two rules most likely to be broken by a well-meaning change: the
// all-or-nothing inheritance from the PRIMARY category (not any category), and the precedence that
// stops a guest being told to "switch to Takeaway" for an item that is switched off everywhere.
// Pure unit test — no DB.
public class OrderTypeAvailabilityTests
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);
    private const string Actor = "availability-tests";

    private static ProductCategory Link(string categoryName, int? categoryMask, bool isPrimary) => new()
    {
        IsPrimary = isPrimary,
        CreatedBy = Actor,
        Category = new Category { Name = categoryName, AvailableOrderTypes = categoryMask, CreatedBy = Actor }
    };

    private static Product Product(
        int? productMask = null,
        int? primaryCategoryMask = null,
        bool hasPrimaryCategory = true,
        bool isAvailable = true,
        int? secondaryCategoryMask = null)
    {
        var product = new Product
        {
            Name = "Dürüm",
            IsAvailable = isAvailable,
            AvailableOrderTypes = productMask,
            CreatedBy = Actor
        };

        if (hasPrimaryCategory)
        {
            product.ProductCategories.Add(Link("Dürüm Wraps", primaryCategoryMask, isPrimary: true));
        }

        if (secondaryCategoryMask is not null)
        {
            product.ProductCategories.Add(Link("Grills", secondaryCategoryMask, isPrimary: false));
        }

        return product;
    }

    [Fact]
    public void Product_inherits_its_primary_categorys_channels()
    {
        var product = Product(productMask: null, primaryCategoryMask: TakeawayAndDelivery);

        OrderTypeAvailability.EffectiveMask(product).Should().Be(TakeawayAndDelivery);
        OrderTypeAvailability.Resolve(product, OrderType.DineIn).CanOrder.Should().BeFalse();
        OrderTypeAvailability.Resolve(product, OrderType.Takeaway).CanOrder.Should().BeTrue();
    }

    [Fact]
    public void Own_mask_overrides_the_category_entirely()
    {
        // Category says takeaway+delivery; the item says dine-in only. Own mask wins outright —
        // inheritance is all-or-nothing, never a merge.
        var product = Product(productMask: (int)OrderChannels.DineIn, primaryCategoryMask: TakeawayAndDelivery);

        OrderTypeAvailability.Resolve(product, OrderType.DineIn).CanOrder.Should().BeTrue();
        OrderTypeAvailability.Resolve(product, OrderType.Takeaway).CanOrder.Should().BeFalse();
    }

    [Fact]
    public void Non_primary_categories_are_ignored_for_inheritance()
    {
        // A second category must NOT widen or narrow the answer — otherwise the effective value
        // changes unpredictably when someone adds a category on an unrelated edit.
        var product = Product(
            productMask: null,
            primaryCategoryMask: TakeawayAndDelivery,
            secondaryCategoryMask: (int)OrderChannels.DineIn);

        OrderTypeAvailability.EffectiveMask(product).Should().Be(TakeawayAndDelivery);
        OrderTypeAvailability.Resolve(product, OrderType.DineIn).CanOrder.Should().BeFalse();
    }

    [Fact]
    public void No_primary_category_falls_back_to_permissive_and_is_flagged_as_a_data_gap()
    {
        var product = Product(productMask: null, hasPrimaryCategory: false);

        OrderTypeAvailability.EffectiveMask(product).Should().BeNull();
        OrderTypeAvailability.Resolve(product, OrderType.DineIn).CanOrder.Should().BeTrue();
        // Permissive, but the admin surface must be able to warn about it.
        OrderTypeAvailability.HasResolvableInheritance(product).Should().BeFalse();
    }

    [Fact]
    public void An_own_mask_needs_no_primary_category_to_be_resolvable()
    {
        var product = Product(productMask: TakeawayAndDelivery, hasPrimaryCategory: false);

        OrderTypeAvailability.HasResolvableInheritance(product).Should().BeTrue();
        OrderTypeAvailability.Resolve(product, OrderType.Takeaway).CanOrder.Should().BeTrue();
    }

    [Fact]
    public void Unavailable_beats_wrong_order_type()
    {
        // Both would block. The reason must be Unavailable, or the client offers a useless
        // "switch to Takeaway" CTA for an item that is off on every channel.
        var product = Product(productMask: TakeawayAndDelivery, isAvailable: false);

        var result = OrderTypeAvailability.Resolve(product, OrderType.DineIn);

        result.CanOrder.Should().BeFalse();
        result.Reason.Should().Be(AvailabilityReason.Unavailable);
    }

    [Fact]
    public void Unavailable_blocks_even_on_a_permitted_channel()
    {
        var product = Product(productMask: TakeawayAndDelivery, isAvailable: false);

        var result = OrderTypeAvailability.Resolve(product, OrderType.Takeaway);

        result.CanOrder.Should().BeFalse();
        result.Reason.Should().Be(AvailabilityReason.Unavailable);
    }

    // The dominant browse state: no order type chosen yet. Nothing may be dimmed, but the chip
    // still needs the allowed set.
    [Fact]
    public void A_null_requested_order_type_never_blocks_but_still_reports_the_allowed_set()
    {
        var product = Product(productMask: TakeawayAndDelivery);

        var result = OrderTypeAvailability.Resolve(product, requestedOrderType: null);

        result.CanOrder.Should().BeTrue();
        result.Reason.Should().Be(AvailabilityReason.Available);
        result.AllowedOrderTypes.Should().Equal(OrderType.Takeaway, OrderType.Delivery);
    }

    [Fact]
    public void Unrestricted_product_reports_every_order_type_as_allowed()
    {
        var product = Product(productMask: null, primaryCategoryMask: null);

        var result = OrderTypeAvailability.Resolve(product, OrderType.DineIn);

        result.CanOrder.Should().BeTrue();
        result.AllowedOrderTypes.Should().Equal(OrderType.DineIn, OrderType.Takeaway, OrderType.Delivery);
    }

    [Fact]
    public void InheritsOrderTypes_reflects_whether_the_item_carries_its_own_mask()
    {
        OrderTypeAvailability.Resolve(Product(productMask: null, primaryCategoryMask: TakeawayAndDelivery), null)
            .InheritsOrderTypes.Should().BeTrue();

        OrderTypeAvailability.Resolve(Product(productMask: TakeawayAndDelivery), null)
            .InheritsOrderTypes.Should().BeFalse();
    }

    [Fact]
    public void Blocked_item_still_reports_where_it_can_be_ordered()
    {
        var product = Product(productMask: TakeawayAndDelivery);

        var result = OrderTypeAvailability.Resolve(product, OrderType.DineIn);

        result.Reason.Should().Be(AvailabilityReason.WrongOrderType);
        // Drives "Dürüm is takeaway & delivery only" + the Switch-to-X CTA.
        result.AllowedOrderTypes.Should().Equal(OrderType.Takeaway, OrderType.Delivery);
    }
}
