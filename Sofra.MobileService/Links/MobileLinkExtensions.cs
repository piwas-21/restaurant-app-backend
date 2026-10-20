using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Links;

public static class MobileLinkExtensions
{
    private const string PaymentReturnHtml = """
        <!doctype html><html lang="en"><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Return to Sofra</title><body><main><h1>Return to Sofra</h1>
        <p>Open Sofra and choose Orders to check your original payment at the restaurant.</p>
        <p>This page does not confirm payment or cancel your order.</p></main></body></html>
        """;

    public static IServiceCollection AddMobileLinks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MobileLinkSettings>, MobileLinkSettingsValidator>();
        services.AddOptions<MobileLinkSettings>()
            .Bind(configuration.GetSection(MobileLinkSettings.SectionName), options => options.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();
        services.AddSingleton<IGetMobilePaymentReturnQueryHandler, GetMobilePaymentReturnQueryHandler>();
        services.AddSingleton<IGetMobileAccountLinkQueryHandler, GetMobileAccountLinkQueryHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapMobileLinks(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous GET carries only opaque navigation references, never payment/account authority.
        endpoints.MapGet("/r/{slug}/payment-return/{reference}",
            (string slug, string reference, HttpContext context, IGetMobilePaymentReturnQueryHandler query) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
                var operations = context.Request.Query["operation"];
                var input = new MobilePaymentReturnQuery(slug, reference, operations.FirstOrDefault(),
                    context.Request.Query.Count, operations.Count, context.Request.Host.ToUriComponent());
                return query.Handle(input) ? Results.Content(PaymentReturnHtml, "text/html; charset=utf-8")
                    : Results.NotFound();
            });
        return endpoints.MapMobileAccountLinks();
    }
}
