using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Directory;

public static class MobileDirectoryExtensions
{
    public static IServiceCollection AddMobileDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MobileDirectorySettings>, MobileDirectorySettingsValidator>();
        services.AddOptions<MobileDirectorySettings>()
            .Bind(configuration.GetSection(MobileDirectorySettings.SectionName), options => options.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGetMobileDirectoryQueryHandler, GetMobileDirectoryQueryHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapMobileDirectory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/directory", (HttpContext context, IGetMobileDirectoryQueryHandler query) =>
        {
            // Expiry belongs to this snapshot. Do not let an intermediary serve a stale active tenant.
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Ok(query.Handle());
        });
        return endpoints;
    }
}
