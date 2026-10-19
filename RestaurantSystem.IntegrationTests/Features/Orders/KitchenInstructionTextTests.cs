using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public class KitchenInstructionTextTests
{
    [Fact]
    public void Unordered_snapshot_storage_does_not_repeat_the_generated_summary()
    {
        var item = new OrderItem
        {
            ProductName = "Tea",
            CreatedBy = nameof(KitchenInstructionTextTests),
            SpecialInstructions = "Add: Mint, Sugar | No: Lemon, Ice | Keep hot",
            IngredientSnapshots = [
                new() { IngredientName = "Sugar", Quantity = 1, IsAddOn = true, CreatedBy = nameof(KitchenInstructionTextTests) },
                new() { IngredientName = "Ice", IsRemoved = true, CreatedBy = nameof(KitchenInstructionTextTests) },
                new() { IngredientName = "Mint", Quantity = 1, IsAddOn = true, CreatedBy = nameof(KitchenInstructionTextTests) },
                new() { IngredientName = "Lemon", IsRemoved = true, CreatedBy = nameof(KitchenInstructionTextTests) }]
        };
        KitchenInstructionText.For(item).Should().Be("Keep hot");
    }

    [Theory]
    [InlineData("Medium Teapot | Add: A Little Sugar | No: Sugar", null)]
    [InlineData("Medium Teapot | Add: A Little Sugar | No: Sugar | Please serve hot", "Please serve hot")]
    [InlineData("Please serve hot | Add: A Little Sugar", "Please serve hot")]
    [InlineData("No: boiling water", "No: boiling water")]
    [InlineData("  Please serve hot  ", "  Please serve hot  ")]
    [InlineData("  No: boiling water | Add: lemon  ", "  No: boiling water | Add: lemon  ")]
    [InlineData("Add: lemon", "Add: lemon")]
    public void Only_proven_structured_summaries_are_suppressed(string text, string? expected)
    {
        var item = new OrderItem
        {
            ProductName = "Tea",
            VariationName = "Medium Teapot",
            SpecialInstructions = text,
            CreatedBy = nameof(KitchenInstructionTextTests),
            IngredientSnapshots = [
                new() { IngredientName = "A Little Sugar", Quantity = 1, IsAddOn = true, CreatedBy = nameof(KitchenInstructionTextTests) },
                new() { IngredientName = "Sugar", IsRemoved = true, CreatedBy = nameof(KitchenInstructionTextTests) }],
        };
        KitchenInstructionText.For(item).Should().Be(expected);
        item.SpecialInstructions.Should().Be(text, "receipt filtering must not rewrite stored instructions");
    }
}
