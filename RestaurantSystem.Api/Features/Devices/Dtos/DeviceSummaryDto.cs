namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>Admin read of one printer-app installation's last-reported fleet status. Carries raw
/// facts only — online/offline and stale-feed are <b>derived at the presentation layer</b> from the
/// timestamps (no threshold baked into the backend). Non-secret config only; never the API key.</summary>
public record DeviceSummaryDto(
    string DeviceId,
    string? Label,
    string? TenantSlug,
    string? Platform,
    string? AppVersion,
    DateTime LastHeartbeatAt,
    bool FeedRunning,
    DateTime? LastSuccessfulPollAt,
    string? ApiBaseUrl,
    string? KitchenPrinter,
    string? CashierPrinter,
    DateTime FirstSeenAt
);
