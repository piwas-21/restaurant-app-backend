using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class ChannelAvailabilityWorker(IServiceScopeFactory scopes, IOptions<TenantBridgeSettings> settings,
    TimeProvider clock, ILogger<ChannelAvailabilityWorker> logger) : BackgroundService
{
    private static readonly TimeSpan MaximumCycleDuration = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var deadline = new CancellationTokenSource(MaximumCycleDuration, clock);
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IChannelAvailabilityProcessor>().Process(cycle.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Provider payloads, tokens and exception text may contain secrets; durable item state holds uncertainty.
                logger.LogError("Sandbox availability synchronization failed; review stock status and source permissions.");
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.AvailabilityPollSeconds), clock, stoppingToken);
        }
    }
}
