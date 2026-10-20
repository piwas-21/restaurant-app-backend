using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutReconciler
{
    /// <summary>Trusted server reconciliation of already-recorded sessions only; never creates a provider request.</summary>
    Task<CheckoutExpirySweepReport> RunAsync(int batchSize, CancellationToken cancellationToken);
}
