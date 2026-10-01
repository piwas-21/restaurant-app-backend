namespace RestaurantSystem.Channels.Api;

/// <summary>Mirrors the additive tenant import wire; no provider credentials or payload body.</summary>
public sealed record TenantOrderRequest(
    string Provider, string StoreId, string ExternalOrderId, string DisplayId,
    string CanonicalOrderHash, string Currency, decimal MerchantTotal, decimal? ReportedTax,
    DateTimeOffset PlacedAt, string FulfillmentType, string? CustomerName, string? CustomerPhone,
    string? Instructions, IReadOnlyList<TenantOrderItem> Items);

public sealed record TenantOrderItem(Guid ProductId, Guid? VariationId, string Name, string? VariationName,
    int Quantity, decimal UnitPrice, decimal Total, string? Instructions);
