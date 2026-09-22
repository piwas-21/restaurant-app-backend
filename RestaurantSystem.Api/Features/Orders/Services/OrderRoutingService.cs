using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Coordinates the separately scoped durable order-routing services.</summary>
public sealed class OrderRoutingService : IOrderRoutingService
{
    private readonly IOrderRoutingLifecycleService _lifecycle;
    private readonly IOrderRoutingReadinessService _readiness;
    private readonly IOrderRoutingAcknowledgementService _acknowledgements;

    public OrderRoutingService(
        IOrderRoutingLifecycleService lifecycle,
        IOrderRoutingReadinessService readiness,
        IOrderRoutingAcknowledgementService acknowledgements)
    {
        _lifecycle = lifecycle;
        _readiness = readiness;
        _acknowledgements = acknowledgements;
    }

    public Task EnsureRoutesAsync(Order order, CancellationToken cancellationToken) =>
        _lifecycle.EnsureRoutesAsync(order, cancellationToken);

    public Task<bool> IsRoutingActivatedAsync(CancellationToken cancellationToken) =>
        _lifecycle.IsRoutingActivatedAsync(cancellationToken);

    public Task BackfillActiveReleasedRoutesAsync(CancellationToken cancellationToken) =>
        _lifecycle.BackfillActiveReleasedRoutesAsync(cancellationToken);

    public Task<IReadOnlyList<OrderRoutingStateDto>> ProjectAsync(
        Guid orderId, CancellationToken cancellationToken) =>
        _lifecycle.ProjectAsync(orderId, cancellationToken);

    public Task ReconcileDeviceRoutesAsync(
        string deviceId, CancellationToken cancellationToken) =>
        _readiness.ReconcileDeviceRoutesAsync(deviceId, cancellationToken);

    public Task ApplyAcknowledgementAsync(
        string deviceId, PrintAckDto acknowledgement, CancellationToken cancellationToken) =>
        _acknowledgements.ApplyAcknowledgementAsync(deviceId, acknowledgement, cancellationToken);
}
