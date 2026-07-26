using FluentAssertions;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

// OrderChannelMap is the ONLY sanctioned conversion between OrderType (1/2/3) and the
// OrderChannels bitmask (1/2/4). These tests are the reason direct casts are forbidden: they pin
// the round-trip for every value and specifically guard the Delivery footgun — (int)OrderType
// .Delivery == 3, which as a mask is the LEGAL value DineIn|Takeaway, so a stray cast throws
// nothing, fails no type check, and silently returns the wrong answer. Pure unit test — no DB.
public class OrderChannelMapTests
{
    [Theory]
    [InlineData(OrderType.DineIn, OrderChannels.DineIn)]
    [InlineData(OrderType.Takeaway, OrderChannels.Takeaway)]
    [InlineData(OrderType.Delivery, OrderChannels.Delivery)]
    public void From_maps_every_order_type_to_its_single_channel(OrderType orderType, OrderChannels expected)
    {
        OrderChannelMap.From(orderType).Should().Be(expected);
    }

    [Theory]
    [InlineData(OrderType.DineIn)]
    [InlineData(OrderType.Takeaway)]
    [InlineData(OrderType.Delivery)]
    public void Round_trip_returns_exactly_the_original_order_type(OrderType orderType)
    {
        var mask = OrderChannelMap.From(orderType);

        OrderChannelMap.ToOrderTypes(mask).Should().Equal(orderType);
    }

    // The footgun, stated as a test: a direct cast of Delivery would produce mask 3, which permits
    // DineIn and Takeaway and forbids Delivery — the exact inverse of the intent.
    [Fact]
    public void Delivery_channel_is_not_the_same_as_the_raw_order_type_value()
    {
        ((int)OrderChannelMap.From(OrderType.Delivery)).Should().Be(4);
        ((int)OrderType.Delivery).Should().Be(3);

        var wrongMask = (OrderChannels)(int)OrderType.Delivery;
        OrderChannelMap.ToOrderTypes(wrongMask).Should().Equal(OrderType.DineIn, OrderType.Takeaway);
    }

    [Fact]
    public void All_contains_every_order_type()
    {
        OrderChannelMap.ToOrderTypes(OrderChannels.All)
            .Should().Equal(OrderType.DineIn, OrderType.Takeaway, OrderType.Delivery);
    }

    [Theory]
    [InlineData(OrderType.DineIn)]
    [InlineData(OrderType.Takeaway)]
    [InlineData(OrderType.Delivery)]
    public void Null_mask_is_unrestricted(OrderType orderType)
    {
        OrderChannelMap.Allows(null, orderType).Should().BeTrue();
        OrderChannelMap.ToOrderTypes((int?)null)
            .Should().Equal(OrderType.DineIn, OrderType.Takeaway, OrderType.Delivery);
    }

    [Fact]
    public void Allows_honours_a_multi_channel_mask()
    {
        var takeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

        OrderChannelMap.Allows(takeawayAndDelivery, OrderType.Takeaway).Should().BeTrue();
        OrderChannelMap.Allows(takeawayAndDelivery, OrderType.Delivery).Should().BeTrue();
        OrderChannelMap.Allows(takeawayAndDelivery, OrderType.DineIn).Should().BeFalse();
    }

    [Fact]
    public void None_blocks_every_order_type()
    {
        foreach (var orderType in Enum.GetValues<OrderType>())
        {
            OrderChannelMap.Allows((int)OrderChannels.None, orderType).Should().BeFalse();
        }
    }

    // All is stored as null so the migration needs no backfill: existing rows are unrestricted.
    [Fact]
    public void ToStoredMask_collapses_All_to_null_and_keeps_partial_sets()
    {
        OrderChannelMap.ToStoredMask(OrderChannels.All).Should().BeNull();
        OrderChannelMap.ToStoredMask(OrderChannels.Takeaway | OrderChannels.Delivery).Should().Be(6);
    }

    [Fact]
    public void From_throws_on_an_unknown_order_type_rather_than_blocking_everything()
    {
        var act = () => OrderChannelMap.From((OrderType)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
