using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class KitchenChangeSnapshotTests
{
    [Fact]
    public void Snapshot_preserves_only_the_delta_and_frozen_nested_customizations()
    {
        var child = Item("Fries", 1);
        var ingredient = new OrderItemIngredientDto
        {
            IngredientId = Guid.NewGuid(),
            IngredientName = "Onion",
            Quantity = 0,
            IsRemoved = true
        };
        var added = Item("Burger", 2) with
        {
            SideItems = [child],
            IngredientCustomizations = [ingredient]
        };
        var json = KitchenChangeSnapshot.Serialize([
            new PrinterFeedChangeDto { Kind = KitchenChangeKind.Add, Current = added }
        ]);
        added.Quantity = 7;
        child.ProductName = "Live catalogue rename";
        ingredient.IsRemoved = false;

        var restored = KitchenChangeSnapshot.Deserialize(json).Single();
        restored.Kind.Should().Be(KitchenChangeKind.Add);
        restored.Previous.Should().BeNull();
        restored.Current!.Quantity.Should().Be(2);
        restored.Current.SideItems.Should().ContainSingle()
            .Which.ProductName.Should().Be("Fries");
        restored.Current.IngredientCustomizations.Should().ContainSingle()
            .Which.IsRemoved.Should().BeTrue();
    }

    [Fact]
    public void Replacement_preserves_both_changed_lines_without_losing_the_void_quantity()
    {
        var before = Item("Original dish", 2);
        var after = Item("Replacement dish", 1);
        var restored = KitchenChangeSnapshot.Deserialize(KitchenChangeSnapshot.Serialize([
            new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.Replace, Previous = before, Current = after
            }
        ])).Single();

        restored.Previous!.Id.Should().Be(before.Id);
        restored.Previous.Quantity.Should().Be(2);
        restored.Current!.Id.Should().Be(after.Id);
        restored.Current.Quantity.Should().Be(1);
    }

    [Theory]
    [InlineData(KitchenChangeKind.Add)]
    [InlineData(KitchenChangeKind.Void)]
    [InlineData(KitchenChangeKind.Replace)]
    [InlineData(KitchenChangeKind.InstructionChange)]
    public void Missing_action_snapshot_is_rejected(KitchenChangeKind kind)
    {
        var action = () => KitchenChangeSnapshot.Serialize([
            new PrinterFeedChangeDto { Kind = kind }
        ]);
        action.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Instruction_change_cannot_disguise_an_item_replacement_or_quantity_change()
    {
        var item = Item("Burger", 1);
        var action = () => KitchenChangeSnapshot.Serialize([
            new PrinterFeedChangeDto
            {
                Kind = KitchenChangeKind.InstructionChange,
                Previous = item,
                Current = item with { Quantity = 2 }
            }
        ]);
        action.Should().Throw<BadRequestException>();
    }

    private static OrderItemDto Item(string name, int quantity) => new()
    {
        Id = Guid.NewGuid(),
        ProductName = name,
        Quantity = quantity
    };
}
