using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Fidelity operations plus the append-only snapshot boundary for accepted native orders.</summary>
public interface IOrderNativeBillingAcceptance : IOrderFidelityCoordinator
{
    Task WriteAcceptedSnapshotAsync(
        Order order,
        string? acceptedCurrency,
        OrderBillingEarningEvaluation? earning,
        OrderBillingRedemptionEvidence? redemption,
        CancellationToken cancellationToken);
}
