using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

/// <summary>An absolute root-item edit, bound to the basket reviewed by this customer.</summary>
public sealed record CustomerBasketItemMutationDto
{
    [JsonRequired]
    public Guid OperationId { get; init; }
    [JsonRequired]
    public Guid ExpectedBasketId { get; init; }
    [JsonRequired]
    public string ExpectedPurchaseFingerprint { get; init; } = string.Empty;
    [JsonRequired]
    public CustomerBasketItemAction Action { get; init; }
    public int? Quantity { get; init; }
    public string? SpecialInstructions { get; init; }
}

public enum CustomerBasketItemAction { Update, Remove }
