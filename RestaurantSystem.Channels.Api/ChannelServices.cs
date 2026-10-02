using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using RestaurantSystem.Channels.Domain;
using RestaurantSystem.Channels.Infrastructure;

namespace RestaurantSystem.Channels.Api;

public static class ChannelServices
{
    public static IServiceCollection AddChannelGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UberWebhookSettings>().Bind(configuration.GetSection(UberWebhookSettings.Section))
            .Validate(s => s.MaxBodyBytes is > 0 and <= 1_048_576 && s.RequestsPerMinute is > 0 and <= 10_000,
                "Webhook size/rate limits must be positive and bounded.")
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(
            configuration.GetConnectionString("Channels") ?? string.Empty));
        services.AddScoped<IWebhookInbox, PostgresWebhookInbox>();
        services.AddScoped<ChannelMediator>();
        services.AddScoped<IChannelCommandHandler<ReceiveUberWebhookCommand, int>, ReceiveUberWebhookCommandHandler>();
        services.AddRateLimiter(o =>
        {
            // 503 triggers Uber's retry policy; requests never enter the inbox when throttled.
            o.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
            o.AddFixedWindowLimiter("uber-webhook", limiter =>
            {
                limiter.PermitLimit = configuration.GetSection(UberWebhookSettings.Section)
                    .Get<UberWebhookSettings>()?.RequestsPerMinute ?? new UberWebhookSettings().RequestsPerMinute;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
        return services;
    }
}
