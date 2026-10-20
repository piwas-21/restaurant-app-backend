using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutEvidenceWriter
{
    /// <summary>Caller has authorized the work and owns its order lock/evidence transaction.</summary>
    Task<CheckoutSessionStatus?> WriteAsync(CustomerOrderCheckoutJournal journal, CustomerOrderCheckoutLease lease,
        StripeCheckoutSession evidence, DateTime now, CancellationToken cancellationToken);
}
