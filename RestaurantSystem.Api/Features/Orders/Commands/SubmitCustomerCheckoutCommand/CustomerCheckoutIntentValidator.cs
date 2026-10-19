using FluentValidation;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCheckoutQuoteQuery;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed class CustomerCheckoutIntentValidator : AbstractValidator<CustomerCheckoutIntentDto>
{
    public CustomerCheckoutIntentValidator()
    {
        RuleFor(value => value.Review).NotNull().SetValidator(new CustomerCheckoutReviewValidator());
        RuleFor(value => value.Contact).NotNull().SetValidator(new CustomerCheckoutContactValidator());
        RuleFor(value => value.ExpectedQuoteFingerprint).Must(BasketPurchaseFingerprint.IsValidDigest);
        RuleFor(value => value.PaymentMethod).Must(method => method is PaymentMethod.Cash or PaymentMethod.CreditCard or PaymentMethod.OnlinePayment);
        RuleFor(value => value.Address!).SetValidator(new CustomerCheckoutAddressValidator());
        RuleFor(value => value.Address).NotNull().When(value => value.Review?.Type == OrderType.Delivery);
        RuleFor(value => value.Address).Null().When(value => value.Review?.Type != OrderType.Delivery);
        RuleFor(value => value.Notes).MaximumLength(OrderFieldLimits.NotesMaxLength);
    }
}
