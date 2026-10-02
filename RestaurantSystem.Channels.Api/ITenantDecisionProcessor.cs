namespace RestaurantSystem.Channels.Api;

public interface ITenantDecisionProcessor
{
    Task<bool> Process(CancellationToken cancellationToken);
}
