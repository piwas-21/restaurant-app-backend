using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Queries.GetCustomerBasketChannelQuery;

public sealed class GetCustomerBasketChannelQueryValidator : AbstractValidator<GetCustomerBasketChannelQuery>
{
    public GetCustomerBasketChannelQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.OrderType).IsInEnum();
    }
}
