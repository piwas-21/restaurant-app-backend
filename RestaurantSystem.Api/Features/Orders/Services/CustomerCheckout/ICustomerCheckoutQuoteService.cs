using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerCheckoutQuoteService
{
    Task<CustomerCheckoutQuoteDto> QuoteAsync(string sessionId, CustomerCheckoutReviewDto review,
        CancellationToken cancellationToken);
}
