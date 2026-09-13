namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Removes a customer's personal data from retained order, delivery-address, and reservation records.
/// The caller owns the surrounding transaction; this service only uses its scoped DbContext.
/// </summary>
public interface IRetainedCustomerDataScrubber
{
    Task ScrubAsync(Guid userId, CancellationToken cancellationToken);
}
