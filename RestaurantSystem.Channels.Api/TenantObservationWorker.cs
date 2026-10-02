using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantObservationWorker(IServiceScopeFactory scopes, IOptions<TenantBridgeSettings> settings,
    TimeProvider clock, ILogger<TenantObservationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ITenantObservationProcessor>().Process(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Exceptions can contain connection strings or provider/customer payloads. Emit a fixed diagnostic only.
                logger.LogError("Sandbox tenant-observation processing failed; its durable lease permits recovery.");
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.PollSeconds), clock, stoppingToken);
        }
    }
}
