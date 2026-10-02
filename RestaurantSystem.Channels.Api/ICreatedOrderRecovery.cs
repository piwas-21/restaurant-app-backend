namespace RestaurantSystem.Channels.Api;

public interface ICreatedOrderRecovery
{
    Task Process(CancellationToken cancellationToken);
}
