namespace RestaurantSystem.Channels.Api;

/// <summary>Mirrors the tenant observation request; identity, state and hash only.</summary>
public sealed record TenantOrderObservation(string Provider, string StoreId, string ExternalOrderId,
    string CanonicalState, string CanonicalHash, DateTimeOffset ObservedAt);
