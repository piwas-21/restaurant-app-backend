using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class CreatedOrderRecoveryWorker(IServiceScopeFactory scopes, IOptions<TenantBridgeSettings> settings,
    TimeProvider clock, ILogger<CreatedOrderRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ICreatedOrderRecovery>().Process(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Never log provider bodies, tokens or exception text. No decisions are made by recovery.
                logger.LogError("Sandbox created-order recovery failed; review its permissions and store backlog.");
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.RecoveryPollSeconds), clock, stoppingToken);
        }
    }
}
