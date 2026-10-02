namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

public sealed record ChannelAvailabilitySnapshot(string Provider, string StoreId, string Currency,
    bool IsSandbox, string Revision, IReadOnlyList<ChannelAvailabilityItem> Items);

public sealed record ChannelAvailabilityItem(Guid ProductId, Guid? VariationId, bool Available, string Reason);
