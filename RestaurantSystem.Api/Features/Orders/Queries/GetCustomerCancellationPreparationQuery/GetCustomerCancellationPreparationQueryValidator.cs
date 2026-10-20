using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCancellationPreparationQuery;

public sealed class GetCustomerCancellationPreparationQueryValidator : AbstractValidator<GetCustomerCancellationPreparationQuery>
{
    public GetCustomerCancellationPreparationQueryValidator()
    {
        RuleFor(value => value.SchemaVersion).NotNull().Equal(1);
        RuleFor(value => value.OrderId).NotEmpty();
    }
}
