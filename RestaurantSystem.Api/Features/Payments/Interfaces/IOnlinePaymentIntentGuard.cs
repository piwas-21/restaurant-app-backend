namespace RestaurantSystem.Api.Features.Payments.Interfaces;

/// <summary>
/// Checks the persisted payment row that authorizes an online checkout attempt.
/// </summary>
public interface IOnlinePaymentIntentGuard
{
    /// <summary>Throws unless the order has an uncaptured online payment intent.</summary>
    Task EnsureProcessingAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Reactivates the order intent after an expired checkout attempt was retired.</summary>
    Task ReactivateLatestFailedAsync(Guid orderId, CancellationToken cancellationToken);
}
