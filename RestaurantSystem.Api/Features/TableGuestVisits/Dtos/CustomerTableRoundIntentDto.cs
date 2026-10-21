using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTableRoundIntentDto
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public Guid OperationId { get; init; }
    [JsonRequired]
    public Guid TenantId { get; init; }
    [JsonRequired]
    public Guid ServiceSessionId { get; init; }
    [JsonRequired]
    public long ExpectedAccountRevision { get; init; }
    [JsonRequired]
    public Guid ExpectedBasketId { get; init; }
    [JsonRequired]
    public string ExpectedPurchaseFingerprint { get; init; } = string.Empty;
    public string? PayloadHash { get; init; }
    public string? OwnerProof { get; init; }
}
