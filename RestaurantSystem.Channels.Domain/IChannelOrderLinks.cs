namespace RestaurantSystem.Channels.Domain;

public interface IChannelOrderLinks
{
    Task<bool> IsImported(string clientId, Guid storeId, string tenantId, Guid externalOrderId, Guid tenantOrderId, CancellationToken cancellationToken);
}
