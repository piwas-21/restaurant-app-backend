namespace RestaurantSystem.Api.Features.Orders.Interfaces;

/// <summary>Allocates the human-facing daily order number (yyyyMMdd + 4-digit sequence).</summary>
public interface IOrderNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
