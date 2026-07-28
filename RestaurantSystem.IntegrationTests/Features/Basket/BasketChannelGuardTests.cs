using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// The add-to-basket half of per-order-type availability. The MESSAGE and the ERROR CODE are both
// part of the contract here, not incidental: the frontend re-displays the message verbatim, and it
// does so only when the code says this rejection is the channel guard — otherwise a guest would see
// whatever any other 400 on the endpoint happens to say. Pure unit test — no DB.
public class BasketChannelGuardTests
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);
    private const string Actor = "channel-guard-tests";

    private static Product Product(int? productMask = null, int? categoryMask = null) =>
        new()
        {
            Name = "Dürüm",
            IsAvailable = true,
            AvailableOrderTypes = productMask,
            CreatedBy = Actor,
            ProductCategories =
            {
                new ProductCategory
                {
                    IsPrimary = true,
                    CreatedBy = Actor,
                    Category = new Category
                    {
                        Name = "Dürüm Wraps",
                        AvailableOrderTypes = categoryMask,
                        CreatedBy = Actor
                    }
                }
            }
        };

    [Fact]
    public void Permits_the_add_when_the_basket_has_no_order_type_yet()
    {
        // The dominant browse state: the guest has not picked a channel, so nothing may block them.
        var act = () => BasketChannelGuard.EnsureOrderable(Product(categoryMask: TakeawayAndDelivery), null);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(OrderType.Takeaway)]
    [InlineData(OrderType.Delivery)]
    public void Permits_the_add_on_a_channel_the_item_allows(OrderType orderType)
    {
        var act = () => BasketChannelGuard.EnsureOrderable(Product(categoryMask: TakeawayAndDelivery), orderType);

        act.Should().NotThrow();
    }

    [Fact]
    public void Permits_the_add_when_neither_item_nor_category_restricts()
    {
        var act = () => BasketChannelGuard.EnsureOrderable(Product(), OrderType.DineIn);

        act.Should().NotThrow();
    }

    [Fact]
    public void Blocks_an_inherited_restriction_and_names_the_channels_that_DO_work()
    {
        var act = () => BasketChannelGuard.EnsureOrderable(Product(categoryMask: TakeawayAndDelivery), OrderType.DineIn);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("Dürüm is not available for DineIn. Available for: Takeaway, Delivery.");
    }

    [Fact]
    public void Tags_the_rejection_with_a_code_so_the_client_can_safely_re_display_the_message()
    {
        // Without this the client can only choose between showing EVERY 400's message (leaking
        // "Session ID is required" and the generic "Validation failed" wrapper to a guest) and
        // showing none of them — which throws away the reason this feature exists to deliver.
        var act = () => BasketChannelGuard.EnsureOrderable(Product(categoryMask: TakeawayAndDelivery), OrderType.DineIn);

        act.Should()
            .Throw<BadRequestException>()
            .Which.ErrorCode.Should()
            .Be(ErrorCodes.OrderTypeNotAvailable);
    }

    [Fact]
    public void An_item_override_beats_its_category_in_both_directions()
    {
        // Category allows takeaway+delivery, the item explicitly allows dine-in only.
        var dineInOnly = Product(productMask: (int)OrderChannels.DineIn, categoryMask: TakeawayAndDelivery);

        var blocked = () => BasketChannelGuard.EnsureOrderable(dineInOnly, OrderType.Takeaway);
        var allowed = () => BasketChannelGuard.EnsureOrderable(dineInOnly, OrderType.DineIn);

        blocked.Should().Throw<BadRequestException>();
        allowed.Should().NotThrow();
    }

    /// <summary>
    /// §9.14: the enforcement boundary must NOT tighten when a primary category is soft-deleted. The
    /// guards share <c>OrderTypeAvailability</c> with the catalog projections, so making the resolver
    /// ignore deleted categories moved this verdict too — deliberately, and in the permissive
    /// direction. In practice the guards load products with the query filters ON, so the join row was
    /// already gone and this was always the answer; pinned because "the guard silently went
    /// permissive and looked done" is the failure mode this feature keeps producing, and a verdict
    /// that depends on which filters ran is exactly how it happens.
    /// </summary>
    [Fact]
    public void Permits_the_add_when_the_restricting_category_has_been_soft_deleted()
    {
        var product = Product(categoryMask: TakeawayAndDelivery);
        product.ProductCategories.First().Category.IsDeleted = true;

        var act = () => BasketChannelGuard.EnsureOrderable(product, OrderType.DineIn);

        act.Should().NotThrow();
    }
}
