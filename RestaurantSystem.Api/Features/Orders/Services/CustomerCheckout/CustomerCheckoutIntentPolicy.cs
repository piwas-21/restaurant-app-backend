using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields;
using RestaurantSystem.Api.Features.Settings.FormFields.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutIntentPolicy(IFormFieldRequirementService fields) : ICustomerCheckoutIntentPolicy
{
    public async Task EnsureAsync(CustomerCheckoutIntentDto intent, CustomerCheckoutQuoteDto quote,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(intent.ExpectedQuoteFingerprint, quote.QuoteFingerprint, StringComparison.Ordinal))
            throw new ConflictException("Checkout changed. Review the current total before placing an order.");
        if (!quote.AvailablePaymentMethods.Contains(intent.PaymentMethod))
            throw new ConflictException("This payment method is no longer available. Review checkout.");
        await fields.EnsureRequiredFieldsPresentAsync(FormFieldRegistry.FormKeys.CheckoutContact,
            new Dictionary<string, string?>
            {
                [FormFieldRegistry.CheckoutContactFields.Name] = intent.Contact.Name,
                [FormFieldRegistry.CheckoutContactFields.Email] = intent.Contact.Email,
                [FormFieldRegistry.CheckoutContactFields.Phone] = intent.Contact.Phone
            }, cancellationToken);
        if (intent.Review.Type != OrderType.Delivery) return;
        var address = intent.Address ?? throw new BadRequestException("A delivery address is required.");
        await fields.EnsureRequiredFieldsPresentAsync(FormFieldRegistry.FormKeys.DeliveryAddress,
            new Dictionary<string, string?>
            {
                [FormFieldRegistry.DeliveryAddressFields.Street] = address.Street,
                [FormFieldRegistry.DeliveryAddressFields.City] = address.City,
                [FormFieldRegistry.DeliveryAddressFields.PostalCode] = address.PostalCode,
                [FormFieldRegistry.DeliveryAddressFields.Country] = address.Country,
                [FormFieldRegistry.DeliveryAddressFields.AdditionalInfo] = address.AdditionalInfo
            }, cancellationToken);
    }
}
