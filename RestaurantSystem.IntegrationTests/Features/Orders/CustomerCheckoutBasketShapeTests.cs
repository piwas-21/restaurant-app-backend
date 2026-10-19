using FluentAssertions;
using System.Text.Json;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Entities;
using DomainBasket = RestaurantSystem.Domain.Entities.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerCheckoutBasketShapeTests
{
    private static BasketItem Row(Guid? parent = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ParentBasketItemId = parent,
        Quantity = 1,
        CreatedBy = "quote-shape"
    };
    private static DomainBasket Basket(params BasketItem[] rows) => new() { Items = rows.ToList(), CreatedBy = "quote-shape" };

    [Fact]
    public void Malformed_ownership_trees_and_duplicate_ids_are_refused_before_recursive_mapping()
    {
        var root = Row();
        var child = Row(root.Id);
        var unknownParent = Row(Guid.NewGuid());
        var duplicate = Row(id: root.Id);
        var cyclic = Row();
        cyclic.ParentBasketItemId = cyclic.Id;
        var invalid = new[] { Basket(root, unknownParent), Basket(root, duplicate), Basket(cyclic), Basket(Row(id: Guid.Empty)) };
        foreach (var basket in invalid)
        {
            var act = () => CustomerCheckoutBasketShape.Ensure(basket);
            act.Should().Throw<ConflictException>();
        }
        CustomerCheckoutBasketShape.Ensure(Basket(child, root)); // Hostile flat enumeration is valid.
    }

    [Fact]
    public void Depth_and_row_budgets_refuse_excess_without_truncation()
    {
        var rows = new List<BasketItem> { Row() };
        for (var depth = 1; depth <= 8; depth++) rows.Add(Row(rows[^1].Id));
        CustomerCheckoutBasketShape.Ensure(Basket(rows.ToArray()));
        rows.Add(Row(rows[^1].Id));
        var tooDeep = () => CustomerCheckoutBasketShape.Ensure(Basket(rows.ToArray()));
        tooDeep.Should().Throw<ConflictException>();
        var wide = Enumerable.Range(0, 1000).Select(_ => Row()).ToArray();
        CustomerCheckoutBasketShape.Ensure(Basket(wide));
        var tooWide = () => CustomerCheckoutBasketShape.Ensure(Basket([.. wide, Row()]));
        tooWide.Should().Throw<ConflictException>();
    }
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[null]")]
    public void Unreadable_persisted_sides_cannot_be_treated_as_no_selection(string json)
    {
        var root = Row();
        root.SelectedSideItemsJson = json;
        var act = () => CustomerCheckoutBasketShape.Ensure(Basket(root));
        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void A_mapper_must_preserve_every_persisted_side_identity_and_quantity()
    {
        var root = Row();
        var side = new SelectedSideItemDto { Id = Guid.NewGuid(), Quantity = 2 };
        root.SelectedSideItemsJson = JsonSerializer.Serialize(new[] { side });
        var basket = Basket(root);
        CustomerCheckoutBasketShape.Ensure(basket);
        var mapped = new BasketItemDto { Id = root.Id, SelectedSideItems = [] };
        var view = new BasketDto { Items = [mapped] };
        var incomplete = () => CustomerCheckoutBasketShape.EnsureView(basket, view);
        incomplete.Should().Throw<ConflictException>();
        mapped.SelectedSideItems.Add(new() { Id = side.Id, Quantity = 2 });
        CustomerCheckoutBasketShape.EnsureView(basket, view);
        mapped.SelectedSideItems[0].Quantity = 1;
        incomplete.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"00000000-0000-0000-0000-000000000000\":1}")]
    public void Unreadable_or_unidentified_ingredient_quantities_cannot_be_dropped(string json)
    {
        var root = Row();
        root.IngredientQuantitiesJson = json;
        var act = () => CustomerCheckoutBasketShape.Ensure(Basket(root));
        act.Should().Throw<ConflictException>();
    }

}
