using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerCheckoutProjectionTests
{
    private static OrderItem Row(Guid? parent = null) => new()
    {
        Id = Guid.NewGuid(),
        ProductName = "Quoted line",
        Quantity = 1,
        ParentOrderItemId = parent,
        CreatedBy = "quote-projection"
    };
    private static CustomerCheckoutReviewContext Context() => new(new()
    {
        Id = Guid.NewGuid(),
        PurchaseFingerprint = new string('a', 64),
        Items = [new BasketItemDto { Id = Guid.NewGuid() }]
    }, null, "CHF", [PaymentMethod.Cash]);
    private static CustomerCheckoutQuoteDto Project(CustomerCheckoutReviewContext context, params OrderItem[] rows) =>
        CustomerCheckoutProjection.Create(context, new() { Type = OrderType.Takeaway },
            new() { Items = rows.ToList(), CreatedBy = "quote-projection" });

    [Fact]
    public void Transient_order_ids_are_omitted_and_do_not_change_the_review_fingerprint()
    {
        var context = Context();
        var firstRoot = Row();
        var first = Project(context, firstRoot, Row(firstRoot.Id));
        var secondRoot = Row();
        var second = Project(context, secondRoot, Row(secondRoot.Id));
        first.Should().BeEquivalentTo(second);
        first.Items[0].BasketItemId.Should().Be(context.Basket.Items[0].Id);
        first.Items[0].Children.Should().ContainSingle().Which.BasketItemId.Should().BeNull();
    }

    [Fact]
    public void Effective_component_parent_preserves_the_reviewed_nested_hierarchy()
    {
        var root = Row();
        var component = Row(root.Id);
        var extra = Row(root.Id);
        extra.ParentComponentOrderItemId = component.Id;
        var result = Project(Context(), root, extra, component);
        result.Items[0].Children.Should().ContainSingle().Which.Children.Should().ContainSingle();
    }

    [Fact]
    public void Missing_roots_or_unreachable_and_duplicate_rows_never_produce_a_partial_quote()
    {
        var root = Row();
        var duplicate = Row(root.Id);
        duplicate.Id = root.Id;
        var invalid = new[] { new[] { root, Row() }, new[] { root, Row(Guid.NewGuid()) }, new[] { root, duplicate } };
        foreach (var rows in invalid)
        {
            var act = () => Project(Context(), rows);
            act.Should().Throw<ConflictException>();
        }
        var missingRootId = Context();
        missingRootId.Basket.Items[0].Id = null;
        var missingId = () => Project(missingRootId, root);
        missingId.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Shared_input_walk_preserves_preorder_and_visits_every_nested_selected_product()
    {
        var first = new CreateOrderItemDto { ProductId = Guid.NewGuid() };
        var nested = new CreateOrderItemDto { ProductId = Guid.NewGuid() };
        var sibling = new CreateOrderItemDto { ProductId = Guid.NewGuid() };
        var second = new CreateOrderItemDto { MenuId = Guid.NewGuid() };
        first.ChildItems = [new() { ChildItems = [nested] }, sibling];
        OrderInputTree.Flatten([first, second]).Should().Equal(first, first.ChildItems[0], nested, sibling, second);
    }
}
