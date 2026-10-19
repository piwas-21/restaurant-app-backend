using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutCommandFactory
{
    internal static CreateOrderCommand Create(CustomerCheckoutIntentDto intent, CustomerCheckoutPrepared prepared,
        CustomerCheckoutQuoteDto quote) => new()
        {
            Type = intent.Review.Type,
            CustomerName = intent.Contact.Name,
            CustomerEmail = intent.Contact.Email,
            CustomerPhone = intent.Contact.Phone,
            Tip = intent.Review.Tip,
            PointsToRedeem = intent.Review.PointsToRedeem,
            Notes = intent.Notes,
            ItemsAreServerPriced = true,
            Items = prepared.Items.ToList(),
            Payments = [new() { PaymentMethod = intent.PaymentMethod, Amount = quote.Money.Total }],
            DeliveryAddress = intent.Address is null ? null : new()
            {
                AddressLine1 = intent.Address.Street,
                City = intent.Address.City,
                PostalCode = intent.Address.PostalCode,
                Country = intent.Address.Country,
                DeliveryInstructions = intent.Address.AdditionalInfo,
                Phone = intent.Contact.Phone
            }
        };
}
