using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerHistoryReceiptQuery;

public sealed class GetCustomerHistoryReceiptQueryValidator : AbstractValidator<GetCustomerHistoryReceiptQuery>
{
    public GetCustomerHistoryReceiptQueryValidator()
    {
        RuleFor(value => value.SchemaVersion).NotNull().Equal(1);
        RuleFor(value => value.OrderId).NotEmpty();
    }
}
