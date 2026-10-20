using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed record CustomerOrderCheckoutLease(Guid Id, CheckoutSessionRequest Request, string? ProviderSessionId,
    DateTime LeaseExpiresAt, DateTime MaximumCreateRetryAt);

public interface ICustomerOrderCheckoutLeaseStore
{
    Task<CustomerOrderCheckoutLease> AcquireAsync(string sessionId, CustomerOrderRecoveryDto proof,
        CancellationToken cancellationToken);
    Task<bool> RecordAsync(string sessionId, CustomerOrderRecoveryDto proof, CustomerOrderCheckoutLease lease,
        StripeCheckoutSession evidence, CancellationToken cancellationToken);
}
