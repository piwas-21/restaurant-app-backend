using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetTableBillQuery;

public class GetTableBillQueryValidator : AbstractValidator<GetTableBillQuery>
{
    public GetTableBillQueryValidator()
    {
        RuleFor(x => x.TableNumber)
            .GreaterThan(0)
            .WithMessage("Table number must be greater than 0");
    }
}
