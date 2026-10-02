namespace RestaurantSystem.Channels.Domain;

public interface IChannelImportStatus
{
    public const int PageSize = 50;
    Task<IReadOnlyList<ChannelImportStatus>> Read(string clientId, Guid storeId, string tenantId, ChannelImportCursor? cursor, CancellationToken cancellationToken);
}

public sealed record ChannelImportStatus(Guid OrderId, string CatalogueRevision, string State, int Attempts,
    string Code, Guid? TenantOrderId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Priority);

public sealed record ChannelImportCursor(int Priority, DateTimeOffset CreatedAt, Guid OrderId);
