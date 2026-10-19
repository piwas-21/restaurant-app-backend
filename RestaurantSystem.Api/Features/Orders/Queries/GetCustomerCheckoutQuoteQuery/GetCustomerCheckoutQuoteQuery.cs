using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerCheckoutQuoteQuery;

public sealed record GetCustomerCheckoutQuoteQuery(string SessionId, CustomerCheckoutReviewDto Review)
    : IQuery<ApiResponse<CustomerCheckoutQuoteDto>>;

public sealed class GetCustomerCheckoutQuoteQueryHandler(ICustomerCheckoutQuoteService service)
    : IQueryHandler<GetCustomerCheckoutQuoteQuery, ApiResponse<CustomerCheckoutQuoteDto>>
{
    public async Task<ApiResponse<CustomerCheckoutQuoteDto>> Handle(GetCustomerCheckoutQuoteQuery query,
        CancellationToken cancellationToken) => ApiResponse<CustomerCheckoutQuoteDto>.SuccessWithData(
            await service.QuoteAsync(query.SessionId, query.Review, cancellationToken));
}
