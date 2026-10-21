using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed record CustomerTableRoundReview(CustomerCheckoutReviewContext Context, CustomerCheckoutReviewDto Input,
    CustomerCheckoutPrepared Prepared, CustomerCheckoutQuoteDto Quote, string? TableLabel);

public interface ICustomerTableRoundQuotes
{
    Task<CustomerTableRoundReview> ReviewAsync(string sessionId, CustomerTableRoundOwner owner,
        CustomerTableRoundIntentDto request, TableServiceSession session, CancellationToken cancellationToken);
}
