namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>
/// The compact per-tenant snapshot FleetSummaryPushService POSTs to sofra's fleet ingest route.
/// Mirrors sofra's <c>fleetPushSchema</c> (camelCase on the wire). Non-PII: device roster + counts
/// only — never customer data, API keys, or raw orders.
/// </summary>
public record FleetPushPayload(
    string TenantSlug,
    DateTime ReportedAt,
    int MissedOrders,
    int RecentErrors,
    IReadOnlyList<FleetPushDevice> Devices);

public record FleetPushDevice(
    string DeviceId,
    string? Label,
    string? Platform,
    string? AppVersion,
    bool FeedRunning,
    DateTime? LastHeartbeatAt,
    DateTime? LastSuccessfulPollAt,
    string? ApiBaseUrl,
    string? KitchenPrinter,
    string? CashierPrinter);
