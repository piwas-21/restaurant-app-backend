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
/// The guarantee is <b>404 once the caller clears authentication and role checks</b> —
/// not 404 for everyone. <c>AuthorizeAttribute</c> (and every <c>Require*</c> derived
/// from it) is endpoint METADATA consumed by AuthorizationMiddleware, which runs ahead
/// of the MVC filter pipeline, so on an authorized endpoint a guest still gets 401 and
/// a wrong-role caller still gets 403 without this filter ever running. That is fine:
/// neither answer varies with the module, so neither reveals what the tenant bought.
/// On an endpoint with no authorize metadata — the public reservations form, the
/// printer feed — this filter IS the first thing a caller meets and the 404 is what
/// they see. Both paths are pinned in ModuleEnforcementEndpointTests.
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
