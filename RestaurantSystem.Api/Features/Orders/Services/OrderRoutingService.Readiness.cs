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

        var states = await _context.OrderRoutingStates
            .Where(state => !state.Order.IsDeleted
                && state.Order.IsKitchenReleased
                && state.Order.Status != OrderStatus.Cancelled
                && state.Order.Status != OrderStatus.Refunded
                && state.Order.Status != OrderStatus.Completed
                && (state.DeviceId == deviceId
                    || (state.DeviceId == null && state.Status == DevicePrintStatus.NotConfigured)))
            .ToListAsync(cancellationToken);
        var changed = false;

        foreach (var state in states)
        {
            var isAssignedToCaller = state.DeviceId == deviceId;
            var isReady = await IsDeviceReadyForTargetAsync(deviceId, state.Target, cancellationToken);

            if (isAssignedToCaller && state.Status == DevicePrintStatus.Queued && !isReady)
            {
                state.DeviceId = null;
                state.Status = DevicePrintStatus.NotConfigured;
                state.Version++;
                state.UpdatedAt = DateTime.UtcNow;
                state.UpdatedBy = _currentUser.GetAuditIdentifier();
                changed = true;
                continue;
            }

            if (isAssignedToCaller
                && state.Status is (DevicePrintStatus.Received or DevicePrintStatus.Sent)
                && !isReady)
            {
                // Physical output may already have happened. Keep ownership and make the next
                // poll retryable, rather than assigning the same job to a second device.
                state.Status = DevicePrintStatus.Unknown;
                state.Version++;
                state.UpdatedAt = DateTime.UtcNow;
                state.UpdatedBy = _currentUser.GetAuditIdentifier();
                changed = true;
                continue;
            }

            if (state.DeviceId is null && state.Status == DevicePrintStatus.NotConfigured && isReady)
            {
                state.DeviceId = deviceId;
                state.Status = DevicePrintStatus.Queued;
                state.Version++;
                state.UpdatedAt = DateTime.UtcNow;
                state.UpdatedBy = _currentUser.GetAuditIdentifier();
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
        }
    }

    private async Task<string?> SelectDeviceAsync(
        DevicePrintTarget target, CancellationToken cancellationToken)
    {
        if (!_modules.IsEnabled(ModuleIds.Printing))
        {
            return null;
        }

        var readinessCutoff = DateTime.UtcNow.AddMinutes(-_settings.HeartbeatFreshnessMinutes);
        var capabilities = await _context.PrinterDeviceTargetCapabilities
            .AsNoTracking()
            .Where(capability => capability.Target == target
                && capability.ReportedAt >= readinessCutoff
                && capability.IsSupported && capability.IsConfigured && capability.AutoPrintEnabled)
            .Join(_context.PrinterDevices.AsNoTracking(), capability => capability.DeviceId,
                device => device.DeviceId, (capability, device) => new { capability, device })
            .Where(pair => pair.device.FeedRunning
                && pair.device.LastHeartbeatAt >= readinessCutoff
                && pair.device.LastSuccessfulPollAt >= readinessCutoff)
            .OrderByDescending(pair => pair.capability.ReportedAt)
            .Select(pair => pair.capability.DeviceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (capabilities is not null)
        {
            return capabilities;
        }

        // Capability-less installations are legacy broadcast clients. They may continue using the
        // headerless feed, but must never become the durable owner of a routed job.
        return null;
    }

    private async Task ReconcileReadinessAsync(Order order, CancellationToken cancellationToken)
    {
        var states = await _context.OrderRoutingStates
            .Where(state => state.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        var changed = false;
        foreach (var state in states)
        {
            if (state.DeviceId is not null
                && state.Status == DevicePrintStatus.Queued
                && !await IsDeviceReadyForTargetAsync(state.DeviceId, state.Target, cancellationToken))
            {
                state.DeviceId = null;
                state.Status = DevicePrintStatus.NotConfigured;
                state.Version++;
                state.UpdatedAt = DateTime.UtcNow;
                state.UpdatedBy = _currentUser.GetAuditIdentifier();
                changed = true;
            }

            if (state.DeviceId is not null
                && state.Status is DevicePrintStatus.Received or DevicePrintStatus.Sent
                && !await IsDeviceReadyForTargetAsync(state.DeviceId, state.Target, cancellationToken))
            {
                // The device may have accepted or sent the ticket before going stale. Keep its
                // binding and surface uncertainty instead of reassigning work that could print twice.
                state.Status = DevicePrintStatus.Unknown;
                state.Version++;
                state.UpdatedAt = DateTime.UtcNow;
                state.UpdatedBy = _currentUser.GetAuditIdentifier();
                changed = true;
            }

            if (state.Status != DevicePrintStatus.NotConfigured)
            {
                continue;
            }

            var deviceId = await SelectDeviceAsync(state.Target, cancellationToken);
            if (deviceId is null)
            {
                continue;
            }

            state.DeviceId = deviceId;
            state.Status = DevicePrintStatus.Queued;
            state.Version++;
            state.UpdatedAt = DateTime.UtcNow;
            state.UpdatedBy = _currentUser.GetAuditIdentifier();
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another reader won the readiness transition. Its state is already authoritative;
            // clear the losing tracker and let the subsequent projection read it afresh.
            _context.ChangeTracker.Clear();
        }
    }

    private async Task<DeviceKitchenRoutingMode> ResolveRoutingModeAsync(
        CancellationToken cancellationToken)
    {
        var readinessCutoff = DateTime.UtcNow.AddMinutes(-_settings.HeartbeatFreshnessMinutes);
        var devices = await _context.PrinterDevices
            .AsNoTracking()
            .Where(device => device.FeedRunning
                && device.LastHeartbeatAt >= readinessCutoff
                && device.LastSuccessfulPollAt >= readinessCutoff)
            .Select(device => new
            {
                device.DeviceId,
                device.LastHeartbeatAt,
                device.KitchenRoutingMode,
                HasCapabilities = _context.PrinterDeviceTargetCapabilities
                    .Any(capability => capability.DeviceId == device.DeviceId)
            })
            .OrderByDescending(device => device.LastHeartbeatAt)
            .ThenBy(device => device.DeviceId)
            .ToListAsync(cancellationToken);

        var currentDevice = devices.FirstOrDefault();
        if (currentDevice is null || !currentDevice.HasCapabilities)
        {
            return DeviceKitchenRoutingMode.SingleKitchen;
        }

        // A tenant can briefly have old and new printer installations online. Choose the newest
        // ready heartbeat, then DeviceId as a stable tie-breaker, instead of making target sets
        // depend on an unordered Any() over a mixed fleet.
        return currentDevice.KitchenRoutingMode;
    }

    private async Task<bool> IsDeviceReadyForTargetAsync(
        string deviceId, DevicePrintTarget target, CancellationToken cancellationToken)
    {
        var readinessCutoff = DateTime.UtcNow.AddMinutes(-_settings.HeartbeatFreshnessMinutes);
        var device = await _context.PrinterDevices.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DeviceId == deviceId
                && item.FeedRunning
                && item.LastHeartbeatAt >= readinessCutoff
                && item.LastSuccessfulPollAt >= readinessCutoff, cancellationToken);
        if (device is null || !_modules.IsEnabled(ModuleIds.Printing))
        {
            return false;
        }

        var hasCapability = await _context.PrinterDeviceTargetCapabilities.AsNoTracking()
            .AnyAsync(capability => capability.DeviceId == deviceId, cancellationToken);
        if (hasCapability)
        {
            return await _context.PrinterDeviceTargetCapabilities.AsNoTracking()
                .AnyAsync(capability => capability.DeviceId == deviceId
                    && capability.Target == target
                    && capability.ReportedAt >= readinessCutoff
                    && capability.IsSupported
                    && capability.IsConfigured
                    && capability.AutoPrintEnabled, cancellationToken);
        }

        // A device with no capability snapshot is a legacy broadcast client. Its old generic
        // printer settings are deliberately not enough to claim a durable route.
        return false;
    }

    private static bool IsRoutable(Order order) =>
        order.IsKitchenReleased
        && order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed);
}
