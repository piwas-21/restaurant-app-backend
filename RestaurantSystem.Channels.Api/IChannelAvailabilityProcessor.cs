namespace RestaurantSystem.Channels.Api;

public interface IChannelAvailabilityProcessor
{
    Task Process(CancellationToken cancellationToken);
}
