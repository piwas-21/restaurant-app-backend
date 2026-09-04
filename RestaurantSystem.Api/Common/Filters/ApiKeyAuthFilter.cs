using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Filters;

/// <summary>
/// Validates the <c>X-Api-Key</c> header against <c>PrinterSettings:ApiKey</c>. It is the ONLY
/// caller check on the printer fleet's endpoints — the order feed and the three device writes —
/// because those have no user to authorize.
/// </summary>
/// <remarks>
/// <para>
/// It used to FAIL OPEN: an unconfigured key returned early and the endpoint served everyone
/// (#475). That is the wrong direction for the one filter standing in front of
/// <c>GET /api/orders/printer-feed</c>, which returns confirmed orders — customer names, phone
/// numbers, addresses. And it was not a hypothetical shape: <c>ApiKey</c> defaults to <c>""</c> in
/// both appsettings.json and appsettings.Development.json, so a tenant provisioned without one
/// came up serving its order feed to anyone, and every functional check passed, because from the
/// printer-app's point of view an open feed works perfectly.
/// </para>
/// <para>
/// Unconfigured now DENIES everywhere except Development, where working without secrets is a real
/// need and there is no customer data to protect. Note the environment test is
/// <c>IsDevelopment()</c> and nothing else: the integration-test host runs as <c>Test</c>, so the
/// suite exercises the same closed door production does, and configures a real key to get through
/// it (appsettings.Test.json).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ApiKeyAuthFilter : Attribute, IAuthorizationFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// One log line per process for the unconfigured state, not one per request. These endpoints
    /// are POLLED — the printer-app hits the feed every ~5s — and Sentry ships anything at Error
    /// level, so a per-request line would bury real errors under tens of thousands of events a day
    /// on exactly the misconfigured tenant this filter exists to protect. The state being reported
    /// is a static configuration fact; it does not become truer by being repeated.
    /// </summary>
    private static int _unconfiguredLogged;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var settings = services.GetRequiredService<IOptions<PrinterSettings>>().Value;
        var logger = services.GetRequiredService<ILogger<ApiKeyAuthFilter>>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            var development = services.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

            // Said out loud rather than passed over in silence — the whole defect was that this
            // branch left no trace of having opened the door.
            if (Interlocked.Exchange(ref _unconfiguredLogged, 1) == 0)
            {
                if (development)
                {
                    logger.LogWarning(
                        "PrinterSettings:ApiKey is not configured — the printer endpoints are "
                        + "UNAUTHENTICATED. Allowed in Development only.");
                }
                else
                {
                    logger.LogError(
                        "PrinterSettings:ApiKey is not configured — REFUSING every printer request. "
                        + "Set it in the tenant's app-secrets.json; provision-tenant.sh generates one.");
                }
            }

            if (development)
            {
                return;
            }

            context.Result = new UnauthorizedResult();
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var provided)
            || !FixedTimeEquals(provided.ToString(), settings.ApiKey))
        {
            // The security-relevant case, and it used to log nothing at all while the benign
            // misconfiguration above logged on every request. Warning, not Error: a wrong key is
            // most often a device that has not been re-paired after a rotation, and it IS
            // attacker-reachable, so it must not be a lever for flooding the log either — hence
            // no path or key material, and nothing per-request beyond this one line.
            logger.LogWarning("Rejected a printer request carrying no key or the wrong one.");
            context.Result = new UnauthorizedResult();
        }
    }

    /// <summary>
    /// Length-independent comparison so the response time does not leak how much of the key was
    /// right. Ordinary <c>!=</c> on strings short-circuits at the first differing character.
    /// </summary>
    private static bool FixedTimeEquals(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected));
}
