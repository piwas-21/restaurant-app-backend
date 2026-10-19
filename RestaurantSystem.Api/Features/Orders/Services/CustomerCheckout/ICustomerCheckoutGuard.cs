using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerCheckoutGuard
{
    Guid? EnsureOwner(string sessionId);
    Task<CustomerCheckoutReviewContext> ReviewAsync(string sessionId, Guid? userId,
        CustomerCheckoutReviewDto review, CancellationToken cancellationToken);
}
