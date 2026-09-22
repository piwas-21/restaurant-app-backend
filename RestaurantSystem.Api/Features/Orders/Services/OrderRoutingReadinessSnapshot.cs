using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderRoutingReadinessSnapshot
{
    private readonly IReadOnlyDictionary<DevicePrintTarget, string> _selectedDevices;
    private readonly IReadOnlySet<(string DeviceId, DevicePrintTarget Target)> _readyTargets;

    public OrderRoutingReadinessSnapshot(
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
