using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Common.Modules;

/// <summary>
/// Gates an endpoint (or a whole controller) on a product module the tenant bought
/// — sofra ADR-010 / S11. Inert until <c>Modules:Enforce</c> is set for the instance.
///
/// Answers <b>404, not 403</b>: for a module the tenant never bought the surface
/// genuinely does not exist on this instance, and 403 would advertise it. The
/// frontend hides the same routes, so the two agree.
///
/// Runs as an authorization filter so an ungated-out endpoint is 404 for everyone,
/// including anonymous callers — the answer must not depend on who is asking.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireModuleAttribute : Attribute, IAuthorizationFilter
{
    public RequireModuleAttribute(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ArgumentException("A module id is required", nameof(moduleId));
        }
        ModuleId = moduleId;
    }

    public string ModuleId { get; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modules = context.HttpContext.RequestServices.GetRequiredService<ITenantModules>();
        if (modules.IsEnabled(ModuleId)) return;

        // Set the result rather than throwing: ExceptionHandlingMiddleware LogError()s
        // everything it catches, and a module being off is normal operation, not a fault.
        context.Result = new NotFoundObjectResult(
            ApiResponse<object>.FailureWithCode(
                "This feature is not enabled for this restaurant.",
                ErrorCodes.ModuleNotEnabled,
                "Not found"));
    }
}
