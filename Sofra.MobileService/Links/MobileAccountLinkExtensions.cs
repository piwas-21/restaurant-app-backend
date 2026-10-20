namespace Sofra.MobileService.Links;

public static class MobileAccountLinkExtensions
{
    private const string AccountHtml = """
        <!doctype html><html lang="en"><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Open Sofra</title><body><main><h1>Open Sofra</h1>
        <p>Open this email link on the device where you installed Sofra.</p>
        <p>Review the restaurant and complete the account action in the app.</p>
        <p>Opening this page does not change your account or sign you in.</p></main></body></html>
        """;

    public static IEndpointRouteBuilder MapMobileAccountLinks(this IEndpointRouteBuilder endpoints)
    {
        Map(endpoints, "verify");
        Map(endpoints, "recover");
        return endpoints;
    }

    private static void Map(IEndpointRouteBuilder endpoints, string action) =>
        endpoints.MapGet("/r/{slug}/" + action,
            (string slug, HttpContext context, IGetMobileAccountLinkQueryHandler query) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
                var input = new MobileAccountLinkQuery(slug, action, context.Request.Query.Count,
                    context.Request.Host.ToUriComponent());
                return query.Handle(input) ? Results.Content(AccountHtml, "text/html; charset=utf-8") : Results.NotFound();
            });
}
