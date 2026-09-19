using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Delivery-address channel rules for the shared staff counter request payload
/// (<see cref="StaffCounterOrderRequest"/>, reused by the quote and create validators).
///
/// The cashier's New sale page can select the Delivery channel; without these rules a delivery
/// request without an address sailed past validation and only failed deep inside
/// <c>OrderFactory</c> ("Delivery address is required for delivery orders"), while a takeaway or
/// dine-in request carrying an address was accepted and silently ignored. Both shapes are refused
/// here instead, with one message each, so the client sees the reason before any pricing work.
///
/// Extracted from <c>StaffCounterOrderRequestValidator</c>, which sits at the §4 limit of 60 lines;
/// per §4 this rule class is what a validator at its limit extracts into.
/// </summary>
public static class StaffCounterDeliveryAddressRule
{
    public const string RequiredForDeliveryMessage =
        "A delivery counter order requires a delivery address with a street.";
    public const string DeliveryOnlyMessage = "A delivery address is valid only for delivery orders.";

    /// <summary>
    /// Delivery requires an address with a street (inline fields; the saved-address
    /// <c>UseAddressId</c> path still needs a street snapshot once resolved, so an explicit blank
    /// street is refused the same way). Any other channel must not carry one.
    /// </summary>
    public static void ValidateDeliveryAddress(this AbstractValidator<StaffCounterOrderRequest> validator)
    {
        validator.RuleFor(request => request.DeliveryAddress)
            .Must(address => address is not null
                && !string.IsNullOrWhiteSpace(address.AddressLine1))
            .When(request => request.Type == OrderType.Delivery)
            .WithMessage(RequiredForDeliveryMessage);
        validator.RuleFor(request => request.DeliveryAddress)
            .Null()
            .When(request => request.Type != OrderType.Delivery)
            .WithMessage(DeliveryOnlyMessage);
    }
}
