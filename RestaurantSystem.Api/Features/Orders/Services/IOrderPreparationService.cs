using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderPreparationService
{
    Task<int> ResolveEffectiveMinutesAsync(
        Order order, int? authorizedOverrideMinutes, CancellationToken cancellationToken);

    Task<int> ApplyPendingApprovalAsync(
        Order order, int? authorizedOverrideMinutes, CancellationToken cancellationToken);

    Task<int> ApplyAcceptedAsync(
        Order order, int? authorizedOverrideMinutes, DateTime acceptedAtUtc,
        CancellationToken cancellationToken);

    Task<int> ApplyForNewOrderAsync(Order order, DateTime acceptedAtUtc, CancellationToken cancellationToken);

    Task ApplyAcceptedOnKitchenReleaseAsync(
        Order order, DateTime releasedAtUtc, CancellationToken cancellationToken);
}
