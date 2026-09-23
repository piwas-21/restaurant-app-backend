using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateStaffRoundCommand;
using RestaurantSystem.Api.Features.Orders.Commands.QuoteStaffCounterOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;
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

    private static CreateOrderDeliveryAddressDto Address() => new()
    {
        AddressLine1 = "Rue du Grand-Pré 45",
        City = "Genève",
        PostalCode = "1202",
        Country = "Switzerland"
    };

    [Fact]
    public void Positive_points_require_a_registered_customer()
    {
        var result = _requestValidator.Validate(Request(1));
        var customerResult = _requestValidator.Validate(Request(1) with
        {
            CustomerUserId = Guid.NewGuid()
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StaffCounterOrderRequest.PointsToRedeem)
            && error.ErrorMessage == "Points redemption requires a registered customer.");
        customerResult.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Zero_points_remains_a_no_op_and_negative_points_are_rejected()
    {
        _requestValidator.Validate(Request(0)).IsValid.Should().BeTrue();
        _requestValidator.Validate(Request(-1)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Redemption_cannot_exceed_the_shared_single_order_cap()
    {
        var result = _requestValidator.Validate(Request(100_001) with
        {
            CustomerUserId = Guid.NewGuid()
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StaffCounterOrderRequest.PointsToRedeem)
            && error.ErrorMessage == "Cannot redeem more than 100,000 points at once.");
    }

    [Fact]
    public void Notes_at_the_order_column_limit_are_accepted()
    {
        var result = _requestValidator.Validate(
            Request(0) with { Notes = new string('x', OrderFieldLimits.NotesMaxLength) });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Notes_over_the_order_column_limit_are_rejected()
    {
        var result = _requestValidator.Validate(
            Request(0) with { Notes = new string('x', OrderFieldLimits.NotesMaxLength + 1) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StaffCounterOrderRequest.Notes)
            && error.ErrorMessage == $"Notes cannot exceed {OrderFieldLimits.NotesMaxLength} characters.");
    }

    [Fact]
    public void Delivery_without_a_street_address_is_rejected()
    {
        var missing = _requestValidator.Validate(Request(0) with { Type = OrderType.Delivery });
        var blank = _requestValidator.Validate(Request(0) with
        {
            Type = OrderType.Delivery,
            DeliveryAddress = new CreateOrderDeliveryAddressDto { AddressLine1 = "   " }
        });

        missing.IsValid.Should().BeFalse();
        missing.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StaffCounterOrderRequest.DeliveryAddress)
            && error.ErrorMessage == "A delivery counter order requires a delivery address with a street.");
        blank.IsValid.Should().BeFalse();
        blank.Errors.Should().Contain(error =>
            error.ErrorMessage == "A delivery counter order requires a delivery address with a street.");
    }

    [Fact]
    public void Delivery_with_an_explicit_street_address_passes_the_request_rules()
    {
        var result = _requestValidator.Validate(Request(0) with
        {
            Type = OrderType.Delivery,
            DeliveryAddress = Address()
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Takeaway_carrying_a_delivery_address_is_rejected()
    {
        var result = _requestValidator.Validate(Request(0) with { DeliveryAddress = Address() });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(StaffCounterOrderRequest.DeliveryAddress)
            && error.ErrorMessage == "A delivery address is valid only for delivery orders.");
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

    [Fact]
    public void Staff_round_requires_dine_in_session_and_rejects_delivery_address()
    {
        var validator = new CreateStaffRoundCommandValidator();
        var takeaway = new CreateStaffRoundCommand
        {
            ClientOperationId = Guid.NewGuid(),
            ReleaseToKitchen = true,
            Type = OrderType.Takeaway,
            Items = Request(0).Items
        };
        var deliveryAddress = new CreateStaffRoundCommand
        {
            ClientOperationId = Guid.NewGuid(),
            ReleaseToKitchen = true,
            Type = OrderType.DineIn,
            ServiceSessionId = Guid.NewGuid(),
            DeliveryAddress = Address(),
            Items = Request(0).Items
        };

        validator.Validate(takeaway).Errors.Should().Contain(error =>
            error.ErrorMessage == "A staff round must be a dine-in order.");
        validator.Validate(deliveryAddress).Errors.Should().Contain(error =>
            error.ErrorMessage == "A delivery address is valid only for delivery orders.");
    }
}
