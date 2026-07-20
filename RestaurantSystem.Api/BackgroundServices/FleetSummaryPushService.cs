using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Devices.Queries.GetMissedOrdersQuery;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

// Rail 3 of fleet observability: pushes a compact per-tenant snapshot (device roster + missed-order
// and recent-error counts) to the sofra control plane's /api/telemetry/fleet route, so /admin/fleet
// can render it. One-directional (backend → sofra), bearer-authed. Deploys INERT — see
// FleetPushSettings. Non-PII: never sends customer data, API keys, or raw orders.
public class FleetSummaryPushService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FleetSummaryPushService> _logger;
    private readonly FleetPushSettings _settings;

    public FleetSummaryPushService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<FleetSummaryPushService> logger,
        IOptions<FleetPushSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var validUrl = Uri.TryCreate(_settings.SofraIngestUrl, UriKind.Absolute, out var ingestUri)
            && (ingestUri.Scheme == Uri.UriSchemeHttp || ingestUri.Scheme == Uri.UriSchemeHttps);

        if (!_settings.Enabled
            || !validUrl
            || string.IsNullOrWhiteSpace(_settings.Secret)
            || string.IsNullOrWhiteSpace(_settings.TenantSlug))
        {
            _logger.LogInformation(
                "FleetSummaryPushService is disabled (needs Enabled=true + a valid absolute SofraIngestUrl + Secret + TenantSlug).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.PushIntervalMinutes));
        _logger.LogInformation("FleetSummaryPushService starting — pushing to sofra every {Minutes}m.", interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var payload = await BuildPayloadAsync(stoppingToken);
                await PushAsync(payload, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // genuine host shutdown — end the loop quietly.
            }
            catch (Exception ex)
            {
                // Catches an HttpClient TIMEOUT too (TaskCanceledException while NOT shutting down):
                // must be logged + retried next tick, NOT allowed to bubble out of ExecuteAsync, which
                // under the default StopHost behaviour would take the whole API down.
                _logger.LogWarning(ex, "Fleet summary push failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // host shutting down mid-wait.
            }
        }
    }

    internal async Task<FleetPushPayload> BuildPayloadAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();

        var devices = await context.PrinterDevices
            .AsNoTracking()
            .OrderBy(d => d.DeviceId)
            .Select(d => new FleetPushDevice(
                d.DeviceId, d.Label, d.Platform, d.AppVersion, d.FeedRunning,
                d.LastHeartbeatAt, d.LastSuccessfulPollAt, d.ApiBaseUrl, d.KitchenPrinter, d.CashierPrinter))
            .ToListAsync(cancellationToken);

        // Reuse the admin missed-order detector so "missed" stays defined in one place. The query caps
        // at 200, so the count saturates there — fine for a summary ("200" already means "a lot").
        var missed = await mediator.SendQuery(
            new GetMissedOrdersQuery(_settings.MissedOrderGraceMinutes, _settings.MissedOrderLookbackHours),
            cancellationToken);
        var missedCount = missed.Data?.Count ?? 0;

        var errorCutoff = DateTime.UtcNow.AddHours(-Math.Max(1, _settings.RecentErrorWindowHours));
        var recentErrors = await context.DeviceEvents
            .CountAsync(e => e.Level == DeviceEventLevel.Error && e.CreatedAt > errorCutoff, cancellationToken);

        return new FleetPushPayload(_settings.TenantSlug, DateTime.UtcNow, missedCount, recentErrors, devices);
    }

    private async Task PushAsync(FleetPushPayload payload, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.SofraIngestUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Secret);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Never log the payload or the secret — just the status.
            _logger.LogWarning("Fleet summary push returned {StatusCode}.", response.StatusCode);
        }
    }
}
