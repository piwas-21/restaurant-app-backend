using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

public sealed record CustomerBasketChannelDto
{
    [JsonRequired]
    public Guid OperationId { get; init; }
    // Empty identity/digest names a reviewed installation with no server basket yet.
    [JsonRequired]
    public Guid ExpectedBasketId { get; init; }
    [JsonRequired]
    public string ExpectedPurchaseFingerprint { get; init; } = string.Empty;
    [JsonRequired]
    public OrderType OrderType { get; init; }
    [JsonRequired]
    public IReadOnlyList<Guid> ConfirmedConflictItemIds { get; init; } = [];
}
