using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

public sealed class CustomerOrderCancellationIntentValidator : AbstractValidator<CustomerOrderCancellationIntentDto>
{
    public CustomerOrderCancellationIntentValidator()
    {
        RuleFor(value => value.SchemaVersion).Equal(1);
        RuleFor(value => value.OperationId).NotEmpty();
        RuleFor(value => value.OrderId).NotEmpty();
        RuleFor(value => value.ExpectedVersion).InclusiveBetween(1, int.MaxValue - 1);
        RuleFor(value => value.PayloadHash).NotEmpty().Matches(@"\A[0-9a-f]{64}\z");
    }
}
