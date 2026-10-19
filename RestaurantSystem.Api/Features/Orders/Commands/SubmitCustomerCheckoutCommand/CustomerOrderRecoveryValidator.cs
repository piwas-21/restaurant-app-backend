using FluentValidation;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed class CustomerOrderRecoveryValidator : AbstractValidator<CustomerOrderRecoveryDto>
{
    public CustomerOrderRecoveryValidator()
    {
        RuleFor(value => value.SchemaVersion).Equal(1);
        RuleFor(value => value.OperationId).NotEmpty();
        RuleFor(value => value.PayloadHash).Must(BasketPurchaseFingerprint.IsValidDigest);
    }
}
