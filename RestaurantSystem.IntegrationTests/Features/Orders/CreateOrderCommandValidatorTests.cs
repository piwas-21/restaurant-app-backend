using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// PR #67 security review regression guard. The previous validator only
/// asserted <c>Items.NotEmpty()</c>, letting tampered payloads through with
/// non-positive quantities (manipulate totals or bypass payment) or items
/// missing both ProductId and MenuId (silently no-op in the factory).
/// </summary>
public class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_ItemWithZeroQuantity_FailsWithQuantityError()
    {
        var command = BuildCommandWithItems(new CreateOrderItemDto
        {
            ProductId = Guid.NewGuid(),
            Quantity = 0
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Items[0].Quantity" &&
            e.ErrorMessage == "Item quantity must be greater than zero.");
    }

    [Fact]
    public void Validate_ItemWithNegativeQuantity_FailsWithQuantityError()
    {
        var command = BuildCommandWithItems(new CreateOrderItemDto
        {
            ProductId = Guid.NewGuid(),
            Quantity = -1
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Items[0].Quantity" &&
            e.ErrorMessage == "Item quantity must be greater than zero.");
    }

    [Fact]
    public void Validate_ItemMissingBothProductIdAndMenuId_Fails()
    {
        var command = BuildCommandWithItems(new CreateOrderItemDto
        {
            ProductId = null,
            MenuId = null,
            Quantity = 1
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage == "Each item must reference either a ProductId or a MenuId.");
    }

    [Fact]
    public void Validate_ValidItemWithProductId_Passes()
    {
        var command = BuildCommandWithItems(new CreateOrderItemDto
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidItemWithMenuId_Passes()
    {
        var command = BuildCommandWithItems(new CreateOrderItemDto
        {
            MenuId = Guid.NewGuid(),
            Quantity = 2
        });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    private static CreateOrderCommand BuildCommandWithItems(params CreateOrderItemDto[] items) =>
        new()
        {
            Type = OrderType.DineIn,
            TableNumber = 1,
            Items = items.ToList()
        };
}
