using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderRoutingReadinessService : IOrderRoutingReadinessService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderRoutingReadinessSnapshotProvider _snapshotProvider;
    private readonly OrderRoutingSettings _settings;

    public OrderRoutingReadinessService(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        IOrderRoutingReadinessSnapshotProvider snapshotProvider,
        IOptions<OrderRoutingSettings> settings)
    {
        _context = context;
        _currentUser = currentUser;
        _snapshotProvider = snapshotProvider;
        _settings = settings.Value;
    }

    public async Task ReconcileDeviceRoutesAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        var readiness = await _snapshotProvider.LoadAsync(cancellationToken);
        var lastStateId = Guid.Empty;
        while (true)
        {
            var states = await _context.OrderRoutingStates
                .Where(state => state.Id.CompareTo(lastStateId) > 0
                    && !state.Order.IsDeleted
                    && state.Order.IsKitchenReleased
                    && state.Order.Status != OrderStatus.Cancelled
                    && state.Order.Status != OrderStatus.Refunded
                    && state.Order.Status != OrderStatus.Completed
                    && (state.DeviceId == deviceId
                        || (state.DeviceId == null && state.Status == DevicePrintStatus.NotConfigured)))
                .OrderBy(state => state.Id)
                .Take(_settings.ProcessingBatchSize)
                .ToListAsync(cancellationToken);
            if (states.Count == 0)
            {
                return;
            }

            lastStateId = states[^1].Id;
            var changed = false;
            foreach (var state in states)
            {
                changed |= ReconcileDeviceRoute(state, deviceId, readiness);
            }

            if (!changed)
            {
                _context.ChangeTracker.Clear();
                continue;
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                _context.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return;
            }
        }
    }

    public async Task ReconcileReadinessAsync(Order order, CancellationToken cancellationToken)
    {
        var states = await _context.OrderRoutingStates
            .Where(state => state.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        var readiness = await _snapshotProvider.LoadAsync(cancellationToken);
        var changed = false;
        foreach (var state in states)
        {
            if (state.DeviceId is not null
                && state.Status == DevicePrintStatus.Queued
                && !readiness.IsDeviceReadyForTarget(state.DeviceId, state.Target))
            {
                MarkNotConfigured(state);
                changed = true;
            }

            if (state.DeviceId is not null
                && state.Status is DevicePrintStatus.Received or DevicePrintStatus.Sent
                && !readiness.IsDeviceReadyForTarget(state.DeviceId, state.Target))
            {
                MarkUnknown(state);
                changed = true;
            }

            if (state.Status != DevicePrintStatus.NotConfigured)
            {
                continue;
            }

            var deviceId = readiness.SelectDevice(state.Target);
            if (deviceId is null)
            {
                continue;
            }

            MarkQueued(state, deviceId);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
        }
    }

    private bool ReconcileDeviceRoute(
        OrderRoutingState state, string deviceId, OrderRoutingReadinessSnapshot readiness)
    {
        var isAssignedToCaller = state.DeviceId == deviceId;
        var isReady = readiness.IsDeviceReadyForTarget(deviceId, state.Target);

        if (isAssignedToCaller && state.Status == DevicePrintStatus.Queued && !isReady)
        {
            MarkNotConfigured(state);
            return true;
        }

        if (isAssignedToCaller && IsInFlight(state.Status) && !isReady)
        {
            MarkUnknown(state);
            return true;
        }

        if (state.DeviceId is null
            && state.Status == DevicePrintStatus.NotConfigured
            && isReady)
        {
            MarkQueued(state, deviceId);
            return true;
        }

        return false;
    }

    private static bool IsInFlight(DevicePrintStatus status) =>
        status is DevicePrintStatus.Received or DevicePrintStatus.Sent;

    private void MarkNotConfigured(OrderRoutingState state)
    {
        state.DeviceId = null;
        state.Status = DevicePrintStatus.NotConfigured;
        MarkChanged(state);
    }

    private void MarkUnknown(OrderRoutingState state)
    {
        state.Status = DevicePrintStatus.Unknown;
        MarkChanged(state);
    }

    private void MarkQueued(OrderRoutingState state, string deviceId)
    {
        state.DeviceId = deviceId;
        state.Status = DevicePrintStatus.Queued;
        MarkChanged(state);
    }

    private void MarkChanged(OrderRoutingState state)
    {
        state.Version++;
        state.UpdatedAt = DateTime.UtcNow;
        state.UpdatedBy = _currentUser.GetAuditIdentifier();
    }
}
