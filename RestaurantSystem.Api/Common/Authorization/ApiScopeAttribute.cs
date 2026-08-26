namespace RestaurantSystem.Api.Common.Authorization;

/// <summary>
/// Marks an endpoint (or a whole controller) as reachable by a machine API token that holds the
/// named scope (docs/plans/API-TOKENS-PLAN.md §5).
/// </summary>
/// <remarks>
/// Pure METADATA — it enforces nothing on its own. <see cref="ApiTokenScopeFilter"/>, registered
/// globally, reads it and denies every token-authenticated request that does not match.
/// <para>
/// It is INERT for a human caller: a logged-in admin's permissions are unchanged by adding or
/// removing this attribute, so annotating an endpoint can never widen or narrow what staff can do.
/// </para>
/// <para>
/// Absence is a denial, not a gap: an endpoint with no <c>[ApiScope]</c> is unreachable by every
/// token that will ever exist. That is why a new endpoint is machine-invisible by default.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ApiScopeAttribute : Attribute
{
    public ApiScopeAttribute(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A scope is required", nameof(scope));
        }
        Scope = scope;
    }

    /// <summary>The scope that satisfies this endpoint for a token caller.</summary>
    public string Scope { get; }
}
