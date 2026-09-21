using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Owns durable, idempotent order-to-printer routing state.</summary>
public interface IOrderRoutingService
{
    /// <summary>Adds the complete route set for a released order to the current transaction.</summary>
    Task EnsureRoutesAsync(Order order, CancellationToken cancellationToken);

    /// <summary>Reconciles readiness only; this method never creates a missing route row.</summary>
    Task<IReadOnlyList<OrderRoutingStateDto>> ProjectAsync(
        Guid orderId, CancellationToken cancellationToken);

    /// <summary>Assigns or downgrades only the routes relevant to one polling device.</summary>
    Task ReconcileDeviceRoutesAsync(string deviceId, CancellationToken cancellationToken);

    /// <summary>Applies a printer acknowledgement to its durable route, if one exists.</summary>
    Task ApplyAcknowledgementAsync(
        string deviceId, PrintAckDto acknowledgement, CancellationToken cancellationToken);
}
