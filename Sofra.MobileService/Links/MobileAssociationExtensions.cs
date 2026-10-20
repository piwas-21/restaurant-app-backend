using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Links;

public static class MobileAssociationExtensions
{
    public static IServiceCollection AddMobileAssociations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MobileAssociationSettings>, MobileAssociationSettingsValidator>();
        services.AddOptions<MobileAssociationSettings>()
            .Bind(configuration.GetSection(MobileAssociationSettings.SectionName), options => options.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();
        services.AddSingleton<IGetMobileAssociationsQueryHandler, GetMobileAssociationsQueryHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapMobileAssociations(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous public OS-association metadata conveys navigation rights, never customer authority.
        endpoints.MapGet("/.well-known/apple-app-site-association", (HttpContext context, IGetMobileAssociationsQueryHandler query) =>
        {
            Headers(context);
            var document = query.Apple(context.Request.Host.ToUriComponent());
            return document is null ? Results.NotFound() : Results.Json(document);
        });
        endpoints.MapGet("/.well-known/assetlinks.json", (HttpContext context, IGetMobileAssociationsQueryHandler query) =>
        {
            Headers(context);
            var document = query.Android(context.Request.Host.ToUriComponent());
            return document is null ? Results.NotFound() : Results.Json(document);
        });
        return endpoints;
    }

    private static void Headers(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
