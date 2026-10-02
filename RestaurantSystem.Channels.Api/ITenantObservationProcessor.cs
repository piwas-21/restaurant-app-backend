namespace RestaurantSystem.Channels.Api;

public interface ITenantObservationProcessor
{
    Task<bool> Process(CancellationToken cancellationToken);
}
