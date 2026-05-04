using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Filters;

/// <summary>
/// Validates the X-Api-Key header against PrinterSettings:ApiKey.
/// Used to protect the printer-feed endpoint without requiring user auth.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ApiKeyAuthFilter : Attribute, IAuthorizationFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var settings = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<PrinterSettings>>().Value;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return; // not configured — open access (dev default)

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var provided)
            || provided != settings.ApiKey)
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
