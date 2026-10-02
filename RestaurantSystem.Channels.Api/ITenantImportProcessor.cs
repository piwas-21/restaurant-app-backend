namespace RestaurantSystem.Channels.Api;

public interface ITenantImportProcessor
{
    Task<bool> Process(CancellationToken cancellationToken);
}
