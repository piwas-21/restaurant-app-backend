using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed record ChannelAvailabilityPlan(TenantStoreBinding Store, AvailabilityBinding Binding,
    string ClientId, int MaximumWrites);

public interface IChannelAvailabilityPolicy
{
    Task<ChannelAvailabilityPlan?> Resolve(CancellationToken cancellationToken);
    Task<TenantAvailabilitySnapshot> Desired(ChannelAvailabilityPlan plan, CancellationToken cancellationToken);
}
