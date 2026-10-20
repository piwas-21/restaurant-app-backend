using FluentAssertions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CurrentStaffTicketItemsTests
{
    [Fact]
    public void Committed_instructions_and_partial_removals_preserve_original_records()
    {
        var original = new OrderItemDto
        {
            Id = Guid.NewGuid(),
            ProductName = "Kebab",
            Quantity = 2,
            UnitPrice = 13m,
            ItemTotal = 26m,
            SpecialInstructions = "Original instruction"
        };
        var updated = original with { SpecialInstructions = "No onion" };
        OrderAmendmentChangeSnapshot[] changes =
        [
            new(original.Id, OrderAmendmentChangeKind.InstructionChange, 0, 0, true, original, updated),
        ];
        var actual = CurrentStaffTicketItems.Project([original], changes, "EUR");
        actual.Should().ContainSingle().Which.Should().BeEquivalentTo(updated);
        original.Quantity.Should().Be(2);
        original.SpecialInstructions.Should().Be("Original instruction");
    }

    [Fact]
    public void Replacements_remove_source_units_without_duplicating_the_supplement()
    {
        var original = new OrderItemDto { Id = Guid.NewGuid(), Quantity = 1, ItemTotal = 13m };
        var replacement = new OrderItemDto { Id = Guid.NewGuid(), Quantity = 1, ItemTotal = 15m };
        OrderAmendmentChangeSnapshot[] changes =
        [new(original.Id, OrderAmendmentChangeKind.Replace, 1, 1, false, original, replacement)];
        CurrentStaffTicketItems.Project([original], changes, "EUR").Should().BeEmpty();
        CurrentStaffTicketItems.Project([replacement], changes, "EUR").Should().ContainSingle();
    }

    [Theory]
    [InlineData(1, 6.66)]
    [InlineData(2, 6.67)]
    public void Partial_lines_keep_the_exact_remaining_unit_values(int removedOrdinal, decimal expected)
    {
        var original = new OrderItemDto { Id = Guid.NewGuid(), Quantity = 3, ItemTotal = 10m };
        OrderAmendmentChangeSnapshot[] changes =
        [new(original.Id, OrderAmendmentChangeKind.Void, removedOrdinal, 1, false, original, null)];
        CurrentStaffTicketItems.Project([original], changes, "EUR").Single().ItemTotal.Should().Be(expected);
    }
}
