using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class OrderChoiceGroupingTests : IntegrationTestBase
{
    public OrderChoiceGroupingTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public void Shuffled_rows_keep_meat_choices_contiguous_and_preserve_carrier_customizations_and_quantities()
    {
        var parent = new OrderItem
        {
            Id = Guid.NewGuid(),
            CreatedBy = nameof(OrderChoiceGroupingTests),
            ProductName = "Menu Tacos 3",
            Quantity = 1,
            ItemTotal = 19m
        };
        var section = Guid.NewGuid();
        var kebab = Child(parent, "Kebab", 2, section, 0);
        var carrier = Child(parent, "Tacos 3", 1, Guid.NewGuid(), 1);
        carrier.IngredientSnapshots.Add(new OrderItemIngredient
        {
            Id = Guid.NewGuid(),
            CreatedBy = nameof(OrderChoiceGroupingTests),
            IngredientId = Guid.NewGuid(),
            IngredientName = "Viande",
            Quantity = 1,
            IsAddOn = true
        });
        var steak = Child(parent, "Steak", 1, section, 2);
        var fries = Child(parent, "Frites", 1, Guid.NewGuid(), 3);
        var drink = Child(parent, "Cola", 1, Guid.NewGuid(), 4);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CreatedBy = nameof(OrderChoiceGroupingTests),
            OrderNumber = "CHOICE-GROUP",
            Type = OrderType.Takeaway,
            Items = [drink, carrier, steak, parent, kebab, fries]
        };

        using var scope = Factory.Services.CreateScope();
        var dto = scope.ServiceProvider.GetRequiredService<IOrderMappingService>().MapToOrderDto(order);
        var children = dto.Items.Single().SideItems!;
        children.Select(child => child.ProductName).Should().Equal("Kebab", "Steak", "Tacos 3", "Frites", "Cola");
        children.Select(child => child.Quantity).Should().Equal(2, 1, 1, 1, 1);
        children.Take(2).Should().OnlyContain(child => child.SectionId == section);
        children[2].IngredientCustomizations!.Single().IngredientName.Should().Be("Viande");
        children.Should().HaveCount(5);
    }

    [Fact]
    public void Unknown_sections_do_not_merge_same_named_independent_lines()
    {
        var parent = new OrderItem { Id = Guid.NewGuid(), CreatedBy = nameof(OrderChoiceGroupingTests), ProductName = "Combo", Quantity = 1 };
        var first = Child(parent, "Fries", 1, null, 0);
        var middle = Child(parent, "Drink", 1, null, 1);
        var last = Child(parent, "Fries", 2, null, 2);
        var order = new Order { Id = Guid.NewGuid(), CreatedBy = nameof(OrderChoiceGroupingTests), Items = [last, parent, middle, first] };
        using var scope = Factory.Services.CreateScope();
        var children = scope.ServiceProvider.GetRequiredService<IOrderMappingService>()
            .MapToOrderDto(order).Items.Single().SideItems!;
        children.Select(child => child.Id).Should().Equal(first.Id, middle.Id, last.Id);
    }

    private static OrderItem Child(OrderItem parent, string name, int quantity, Guid? sectionId, int offset) => new()
    {
        Id = Guid.NewGuid(),
        CreatedBy = nameof(OrderChoiceGroupingTests),
        ParentOrderItemId = parent.Id,
        ProductName = name,
        Quantity = quantity,
        Kind = OrderItemKind.BundleChild,
        SectionId = sectionId,
        CreatedAt = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc).AddSeconds(offset)
    };
}
