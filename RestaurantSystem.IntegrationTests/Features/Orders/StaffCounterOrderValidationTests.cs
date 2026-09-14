using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>Pure hostile coverage for staff-only request controls.</summary>
public sealed class StaffCounterOrderValidationTests
{
    private readonly StaffCounterOrderRequestValidator _requestValidator = new();

    private static StaffCounterOrderRequest Request(int? points) => new()
    {
        Type = OrderType.Takeaway,
        PointsToRedeem = points,
        Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
    };

    [Fact]
    public void Positive_points_are_rejected_before_staff_pricing()
    {
        var result = _requestValidator.Validate(Request(1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StaffCounterOrderRequest.PointsToRedeem)
            && error.ErrorMessage == "Points redemption is not supported for staff counter orders.");
    }

    [Fact]
    public void Zero_points_remains_a_no_op_and_negative_points_are_rejected()
    {
        _requestValidator.Validate(Request(0)).IsValid.Should().BeTrue();
        _requestValidator.Validate(Request(-1)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Quote_and_create_reuse_the_same_request_rules()
    {
        var quote = new QuoteStaffCounterOrderCommand
        {
            Type = OrderType.Takeaway,
            PointsToRedeem = 1,
            Items = Request(1).Items
        };
        var create = new CreateStaffCounterOrderCommand
        {
            Type = OrderType.Takeaway,
            PointsToRedeem = 1,
            Items = Request(1).Items,
            ClientOperationId = Guid.NewGuid()
        };

        new QuoteStaffCounterOrderCommandValidator().Validate(quote).IsValid.Should().BeFalse();
        new CreateStaffCounterOrderCommandValidator().Validate(create).IsValid.Should().BeFalse();
    }
}
