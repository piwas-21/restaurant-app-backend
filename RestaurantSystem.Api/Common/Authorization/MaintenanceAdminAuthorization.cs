using Microsoft.AspNetCore.Authorization;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Common.Authorization;

/// <summary>
/// Satisfies maintenance access for TWO caller kinds, either of which is enough:
/// a human <c>Admin</c> (the JWT path the admin UI uses — unchanged), or a machine API token
/// holding <see cref="ApiTokenScopes.MaintenanceWrite"/> (2026-09-07: so an operator script can
/// run the per-tenant image backfills without an admin password).
/// </summary>
public class MaintenanceAdminRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "maintenance-admin";
}

public class MaintenanceAdminHandler : AuthorizationHandler<MaintenanceAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MaintenanceAdminRequirement requirement)
    {
        var user = context.User;
        var isAdmin = user.IsInRole("Admin");
        var isMaintenanceToken = user.HasClaim(ApiTokenDefaults.AuthMethodClaimType, ApiTokenDefaults.ApiTokenAuthMethod)
            && user.HasClaim(ApiTokenDefaults.ScopeClaimType, ApiTokenScopes.MaintenanceWrite);

        if (isAdmin || isMaintenanceToken)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
