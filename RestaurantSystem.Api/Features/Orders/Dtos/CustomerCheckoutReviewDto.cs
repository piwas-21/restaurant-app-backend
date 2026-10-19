using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerCheckoutReviewDto
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public Guid ExpectedBasketId { get; init; }
    [JsonRequired]
    public string ExpectedPurchaseFingerprint { get; init; } = string.Empty;
    [JsonRequired]
    public OrderType Type { get; init; }
    [JsonRequired]
    public int PointsToRedeem { get; init; }
    [JsonRequired]
    public decimal Tip { get; init; }
}
