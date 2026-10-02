namespace RestaurantSystem.Channels.Api;

// This isolated service has one app credential; coalesce refreshes across request scopes.
public sealed class TokenRefreshLock : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public void Dispose() => Gate.Dispose();
}
