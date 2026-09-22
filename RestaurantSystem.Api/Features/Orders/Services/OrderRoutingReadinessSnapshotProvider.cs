using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderRoutingReadinessSnapshotProvider : IOrderRoutingReadinessSnapshotProvider
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantModules _modules;
    private readonly OrderRoutingSettings _settings;

    public OrderRoutingReadinessSnapshotProvider(
        ApplicationDbContext context,
        ITenantModules modules,
        IOptions<OrderRoutingSettings> settings)
    {
        _context = context;
        _modules = modules;
        _settings = settings.Value;
    }

    public async Task<OrderRoutingReadinessSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var printingEnabled = _modules.IsEnabled(ModuleIds.Printing);
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
            return Empty();
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

        return printingEnabled
            ? new OrderRoutingReadinessSnapshot(routingMode, selectedDevices, readyTargets)
            : new OrderRoutingReadinessSnapshot(
                routingMode,
                new Dictionary<DevicePrintTarget, string>(),
                new HashSet<(string DeviceId, DevicePrintTarget Target)>());
    }

    private static OrderRoutingReadinessSnapshot Empty() =>
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
