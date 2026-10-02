using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentRequestFactory
{
    internal static StaffCounterOrderRequest CreateSupplement(
        Order source, OrderAmendmentQuoteRequest amendmentRequest)
    {
        var replacementItems = amendmentRequest.Changes
            .Where(change => change.Kind == OrderAmendmentChangeKind.Replace)
            .Select(change => change.Current!)
            .ToList();

        return new StaffCounterOrderRequest
        {
            Type = source.Type,
            TableId = source.TableId,
            TableNumber = source.TableNumber,
            ServiceSessionId = source.ServiceSessionId,
            CustomerUserId = source.UserId,
            CustomerName = source.CustomerName,
            CustomerEmail = source.CustomerEmail,
            CustomerPhone = source.CustomerPhone,
            DeliveryAddress = Address(source.DeliveryAddress),
            Tip = 0m,
            Notes = null,
            PaymentState = StaffOrderPaymentState.Unpaid,
            Items = amendmentRequest.Additions.Concat(replacementItems).ToList()
        };
    }

    private static CreateOrderDeliveryAddressDto? Address(OrderAddress? address) => address is null
        ? null
        : new CreateOrderDeliveryAddressDto
        {
            Label = address.Label,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            City = address.City,
            State = address.State,
            PostalCode = address.PostalCode,
            Country = address.Country,
            Phone = address.Phone,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
            DeliveryInstructions = address.DeliveryInstructions
        };
}
