namespace RestaurantSystem.Api.Features.Devices.Services;

internal static class DeviceIdNormalizer
{
    internal const int MaxLength = 64;

    internal static string? Normalize(string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
}
