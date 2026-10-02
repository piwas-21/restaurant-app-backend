namespace RestaurantSystem.Channels.Domain;

public sealed record ChannelImportJob(string ClientId, Guid StoreId, Guid OrderId, string TenantId,
    string CatalogueRevision, Guid LeaseId, string? EncryptedRequest, DateTimeOffset? PayloadExpiresAt);
