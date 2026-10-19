using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCheckoutQuoteQuery;

public sealed class GetCustomerCheckoutQuoteQueryValidator : AbstractValidator<GetCustomerCheckoutQuoteQuery>
{
    public GetCustomerCheckoutQuoteQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.Review).NotNull().SetValidator(new CustomerCheckoutReviewValidator());
    }
}
