using FluentValidation;

namespace RestaurantSystem.Api.Features.FidelityPoints.Queries.GetCustomerLoyaltyBalanceQuery;

public sealed class GetCustomerLoyaltyBalanceQueryValidator : AbstractValidator<GetCustomerLoyaltyBalanceQuery>
{
    public GetCustomerLoyaltyBalanceQueryValidator() => RuleFor(query => query.SchemaVersion).NotNull().Equal(1);
}
