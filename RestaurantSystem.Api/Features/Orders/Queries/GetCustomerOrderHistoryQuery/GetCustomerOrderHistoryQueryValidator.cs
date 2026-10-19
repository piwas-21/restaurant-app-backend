using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerOrderHistoryQuery;

public sealed class GetCustomerOrderHistoryQueryValidator : AbstractValidator<GetCustomerOrderHistoryQuery>
{
    public GetCustomerOrderHistoryQueryValidator()
    {
        RuleFor(value => value.SchemaVersion).NotNull().Equal(1);
        RuleFor(value => value.BeforeOrderId).NotEqual(Guid.Empty).When(value => value.BeforeOrderId.HasValue);
        RuleFor(value => value).Must(value => value.BeforeOrderDate.HasValue == value.BeforeOrderId.HasValue)
            .WithMessage("Both order cursor fields are required together.");
        RuleFor(value => value.BeforeOrderDate).Must(value => value!.Value.Kind == DateTimeKind.Utc)
            .When(value => value.BeforeOrderDate.HasValue).WithMessage("The order cursor requires a UTC instant.");
    }
}
