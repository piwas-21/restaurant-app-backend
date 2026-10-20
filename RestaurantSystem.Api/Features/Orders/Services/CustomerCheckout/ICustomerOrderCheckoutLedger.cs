using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutLedger
{
    /// <summary>Caller owns the original order lock and evidence transaction. No provider I/O.</summary>
    Task<CheckoutSessionStatus?> ApplyAsync(CustomerOrderCheckoutJournal journal, StripeCheckoutSession evidence,
        DateTime now, CancellationToken cancellationToken);
}
