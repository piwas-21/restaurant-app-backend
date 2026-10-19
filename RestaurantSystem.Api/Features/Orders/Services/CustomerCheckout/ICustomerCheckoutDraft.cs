using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerCheckoutDraft
{
    Task<Order> BuildAsync(CustomerCheckoutReviewContext context, CustomerCheckoutReviewDto review,
        CancellationToken cancellationToken);
}
