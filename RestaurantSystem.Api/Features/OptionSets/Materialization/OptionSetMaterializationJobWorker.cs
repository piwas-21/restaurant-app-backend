using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantFeatures _features;
    private readonly OptionSetAuthoringSettings _settings;
    private readonly ILogger<OptionSetMaterializationJobWorker> _logger;

    public OptionSetMaterializationJobWorker(
        IServiceScopeFactory scopeFactory,
        ITenantFeatures features,
        IOptions<OptionSetAuthoringSettings> settings,
        ILogger<OptionSetMaterializationJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _features = features;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                if (_features.OptionSetMaterializationEnabled)
                {
                    using var scope = _scopeFactory.CreateScope();
                    processed = await scope.ServiceProvider
                        .GetRequiredService<IOptionSetMaterializationJobRunner>()
                        .RunNextBatchAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Option-set materialization job batch failed.");
            }

            if (!processed)
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.JobPollIntervalSeconds), stoppingToken);
            }
        }
    }
}
