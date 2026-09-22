using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed partial class OrderRoutingService
{
    private sealed class RoutingReadinessSnapshot
    {
        private readonly IReadOnlyDictionary<DevicePrintTarget, string> _selectedDevices;
        private readonly IReadOnlySet<(string DeviceId, DevicePrintTarget Target)> _readyTargets;

        public RoutingReadinessSnapshot(
            DeviceKitchenRoutingMode routingMode,
            IReadOnlyDictionary<DevicePrintTarget, string> selectedDevices,
            IReadOnlySet<(string DeviceId, DevicePrintTarget Target)> readyTargets)
        {
            RoutingMode = routingMode;
            _selectedDevices = selectedDevices;
            _readyTargets = readyTargets;
        }

        public DeviceKitchenRoutingMode RoutingMode { get; }

        public string? SelectDevice(DevicePrintTarget target) =>
            _selectedDevices.GetValueOrDefault(target);

        public bool IsDeviceReadyForTarget(string deviceId, DevicePrintTarget target) =>
            _readyTargets.Contains((deviceId, target));
    }

    private async Task<RoutingReadinessSnapshot> LoadReadinessSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (!_modules.IsEnabled(ModuleIds.Printing))
        {
            return EmptyReadinessSnapshot();
        }

        var readinessCutoff = DateTime.UtcNow.AddMinutes(-_settings.HeartbeatFreshnessMinutes);
        var devices = await _context.PrinterDevices
            .AsNoTracking()
            .Where(device => device.FeedRunning
                && device.LastHeartbeatAt >= readinessCutoff
                && device.LastSuccessfulPollAt >= readinessCutoff)
            .Select(device => new ReadyDevice(
                device.DeviceId, device.LastHeartbeatAt, device.KitchenRoutingMode))
            .ToListAsync(cancellationToken);
        if (devices.Count == 0)
        {
            return EmptyReadinessSnapshot();
        }

        var deviceIds = devices.Select(device => device.DeviceId).ToArray();
        var capabilities = await _context.PrinterDeviceTargetCapabilities
            .AsNoTracking()
            .Where(capability => deviceIds.Contains(capability.DeviceId))
            .Select(capability => new CapabilitySnapshot(
                capability.DeviceId,
                capability.Target,
                capability.ReportedAt,
                capability.IsSupported,
                capability.IsConfigured,
                capability.AutoPrintEnabled))
            .ToListAsync(cancellationToken);

        var readyDeviceIds = devices.Select(device => device.DeviceId).ToHashSet();
        var eligible = capabilities
            .Where(capability => capability.ReportedAt >= readinessCutoff
                && capability.IsSupported
                && capability.IsConfigured
                && capability.AutoPrintEnabled
                && readyDeviceIds.Contains(capability.DeviceId))
            .ToList();
        var readyTargets = eligible
            .Select(capability => (capability.DeviceId, capability.Target))
            .ToHashSet();
        var capableDeviceIds = capabilities
            .Select(capability => capability.DeviceId)
            .ToHashSet();
        var selectedDevices = eligible
            .GroupBy(capability => capability.Target)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(capability => capability.ReportedAt)
                    .ThenBy(capability => capability.DeviceId)
                    .Select(capability => capability.DeviceId)
                    .First());

        var newestCapableDevice = devices
            .Where(device => capableDeviceIds.Contains(device.DeviceId))
            .OrderByDescending(device => device.LastHeartbeatAt)
            .ThenBy(device => device.DeviceId)
            .FirstOrDefault();
        var routingMode = newestCapableDevice?.KitchenRoutingMode
            ?? DeviceKitchenRoutingMode.SingleKitchen;

        return new RoutingReadinessSnapshot(routingMode, selectedDevices, readyTargets);
    }

    private static RoutingReadinessSnapshot EmptyReadinessSnapshot() =>
        new(
            DeviceKitchenRoutingMode.SingleKitchen,
            new Dictionary<DevicePrintTarget, string>(),
            new HashSet<(string DeviceId, DevicePrintTarget Target)>());

    private sealed record ReadyDevice(
        string DeviceId, DateTime LastHeartbeatAt, DeviceKitchenRoutingMode KitchenRoutingMode);

    private sealed record CapabilitySnapshot(
        string DeviceId,
        DevicePrintTarget Target,
        DateTime ReportedAt,
        bool IsSupported,
        bool IsConfigured,
        bool AutoPrintEnabled);
}
