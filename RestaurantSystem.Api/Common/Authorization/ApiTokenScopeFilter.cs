using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Common.Authorization;

/// <summary>
/// Deny-by-default scope enforcement for callers authenticated by an API TOKEN
/// (docs/plans/API-TOKENS-PLAN.md §5). Registered globally in <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// A token's reachable surface is a CLOSED ALLOW-LIST: an endpoint is reachable only if it
/// carries an <see cref="ApiScopeAttribute"/> naming a scope the token holds. Everything else is
/// 403 — including every endpoint added after the token was issued, and including
/// <c>/api/ApiTokens</c> itself, so no token can ever mint or read another.
/// <para>
/// This is deliberately NOT the app's general fallback authorization policy, whose absence is
/// backend issue #413. That issue is not fixed here and this filter does not depend on it: it
/// answers only for the <c>ApiToken</c> scheme, and it says NO unless told otherwise, so an
/// endpoint that is under-protected for humans (#413's <c>POST /api/Products</c>) is still
/// unreachable by a token unless we annotated it on purpose.
/// </para>
/// <para>
/// A human JWT caller returns immediately — the filter cannot change what staff may do.
/// </para>
/// </remarks>
public sealed class ApiTokenScopeFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.HttpContext.User;
        var authenticatedByToken = user.Identity?.IsAuthenticated == true &&
            user.HasClaim(
                ApiTokenDefaults.AuthMethodClaimType, ApiTokenDefaults.ApiTokenAuthMethod);

        if (!authenticatedByToken)
        {
            return;
        }

        var required = context.ActionDescriptor.EndpointMetadata
            .OfType<ApiScopeAttribute>()
            .Select(a => a.Scope)
            .ToList();

        // No annotation at all => not part of the machine surface => refused.
        if (required.Count > 0 &&
            required.Any(scope => user.HasClaim(ApiTokenDefaults.ScopeClaimType, scope)))
        {
            return;
        }

        // Set the result rather than throwing: ExceptionHandlingMiddleware LogError()s what it
        // catches, and a token being too narrow is normal operation, not a fault.
        context.Result = new ObjectResult(
            ApiResponse<object>.FailureWithCode(
                required.Count == 0
                    ? "This endpoint is not available to API tokens."
                    : $"This API token is missing the required scope: {string.Join(" or ", required)}.",
                ErrorCodes.MissingScope,
                "Access denied"))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
