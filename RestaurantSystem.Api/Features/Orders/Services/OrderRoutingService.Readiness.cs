using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed partial class OrderRoutingService
{
    public async Task ReconcileDeviceRoutesAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        var readiness = await LoadReadinessSnapshotAsync(cancellationToken);
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
            // Visit every route in the batch. A lazy projection followed by Any would stop after
            // the first change and leave sibling targets unassigned.
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
                // Preserve the old retry semantics: the conflicted page remains eligible for a
                // subsequent poll instead of advancing the cursor past a state we did not save.
                _context.ChangeTracker.Clear();
                return;
            }
        }
    }

    private bool ReconcileDeviceRoute(
        OrderRoutingState state, string deviceId, RoutingReadinessSnapshot readiness)
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
            // Physical output may already have happened. Keep ownership and make the next poll
            // retryable, rather than assigning the same job to a second device.
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

    private async Task ReconcileReadinessAsync(Order order, CancellationToken cancellationToken)
    {
        var states = await _context.OrderRoutingStates
            .Where(state => state.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        var readiness = await LoadReadinessSnapshotAsync(cancellationToken);
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
                // The device may have accepted or sent the ticket before going stale. Keep its
                // binding and surface uncertainty instead of reassigning work that could print twice.
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
            // Another reader won the readiness transition. Its state is already authoritative;
            // clear the losing tracker and let the subsequent projection read it afresh.
            _context.ChangeTracker.Clear();
        }
    }

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

    private static bool IsRoutable(Order order) =>
        order.IsKitchenReleased
        && order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed);
}
