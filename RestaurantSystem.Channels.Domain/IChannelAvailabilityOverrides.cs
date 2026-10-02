namespace RestaurantSystem.Channels.Domain;

public sealed record ChannelAvailabilityOverride(bool IsPaused, DateTimeOffset? PausedUntil,
    Guid ActorId, DateTimeOffset UpdatedAt);

public interface IChannelAvailabilityOverrides
{
    Task<ChannelAvailabilityOverride?> Read(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<ChannelAvailabilityOverride> Set(AvailabilityBinding binding, bool isPaused, DateTimeOffset? pausedUntil,
        Guid actorId, DateTimeOffset now, CancellationToken cancellationToken);
}
