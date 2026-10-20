namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public interface ICustomerOrderCheckoutEffects
{
    /// <summary>Post-commit, retryable effects through existing idempotent loyalty/email authorities.</summary>
    Task CompleteAsync(Guid orderId, CancellationToken cancellationToken);
}
