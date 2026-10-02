namespace RestaurantSystem.Channels.Domain;

public sealed record ChannelObservationJob(string ClientId, Guid StoreId, Guid ExternalOrderId,
    string TenantId, Guid TenantOrderId, Guid LeaseId);
