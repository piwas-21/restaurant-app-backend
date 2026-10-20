using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutCreationPolicy
{
    Task<CheckoutCharge> ReadAsync(Order order, CancellationToken cancellationToken);
}
