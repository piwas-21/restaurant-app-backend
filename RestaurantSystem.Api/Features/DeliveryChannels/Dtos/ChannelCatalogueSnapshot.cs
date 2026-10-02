namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

public sealed record ChannelCatalogueSnapshot(string Provider, string StoreId, string Currency, bool IsSandbox,
    string Language, string Revision, IReadOnlyList<ChannelCatalogueItem> Items);

public sealed record ChannelCatalogueItem(Guid ProductId, Guid? VariationId, string Name, string Description,
    string? VariationName, int? PriceMinor, bool Available, string BlockReason);
