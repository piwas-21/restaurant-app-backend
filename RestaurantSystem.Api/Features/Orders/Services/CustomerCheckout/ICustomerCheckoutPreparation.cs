using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed record CustomerCheckoutAcceptanceReview(CustomerCheckoutReviewContext Context,
    CustomerCheckoutPrepared Prepared, CustomerCheckoutQuoteDto Quote);

public interface ICustomerCheckoutPreparation
{
    Task<CustomerCheckoutPreparationDto> PrepareAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken);
    /// <summary>Caller holds the original basket owner's transaction lock.</summary>
    Task<CustomerCheckoutAcceptanceReview> ReviewUnderLockAsync(string sessionId, CustomerCheckoutIntentDto intent,
        CancellationToken cancellationToken);
}
