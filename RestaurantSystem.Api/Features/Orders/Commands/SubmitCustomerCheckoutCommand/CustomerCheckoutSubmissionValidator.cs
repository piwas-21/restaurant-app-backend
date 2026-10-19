using FluentValidation;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed class CustomerCheckoutSubmissionValidator : AbstractValidator<CustomerCheckoutSubmissionDto>
{
    public CustomerCheckoutSubmissionValidator()
    {
        RuleFor(value => value.SchemaVersion).Equal(1);
        RuleFor(value => value.OperationId).NotEmpty();
        RuleFor(value => value.PayloadHash).Must(BasketPurchaseFingerprint.IsValidDigest);
        RuleFor(value => value.Intent).NotNull().SetValidator(new CustomerCheckoutIntentValidator());
    }
}
