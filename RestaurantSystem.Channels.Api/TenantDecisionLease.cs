using System.Text.Json.Serialization;

namespace RestaurantSystem.Channels.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantDecisionLease
{
    [JsonRequired] public Guid DecisionId { get; init; }
    [JsonRequired] public Guid LeaseId { get; init; }
    [JsonRequired] public Guid OrderId { get; init; }
    [JsonRequired] public DateTimeOffset LeaseUntil { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string StoreId { get; init; } = string.Empty;
    public string ExternalOrderId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int Attempt { get; init; }
}

public sealed record TenantDecisionReport(Guid LeaseId, string State, string CanonicalState,
    string CanonicalHash, DateTimeOffset ObservedAt);
