namespace RestaurantSystem.Api.Features.Orders.Interfaces;

/// <summary>Allocates the human-facing daily order number (yyyyMMdd + 4-digit sequence).</summary>
public interface IOrderNumberGenerator
{
    /// <summary>
    /// Allocates the next order number for today, serialising against any concurrent allocation.
    /// </summary>
    /// <remarks>
    /// <b>Must be called inside an open transaction, and the order must be inserted in that same
    /// transaction.</b> Allocation is guarded by a transaction-scoped lock, so the number is only
    /// reserved for as long as the caller's transaction lives; allocating in one transaction and
    /// inserting in another reopens the collision this guard exists to close.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The context has no active transaction, so the allocation could not be guarded.
    /// </exception>
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
