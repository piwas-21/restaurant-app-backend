using System.Diagnostics.Contracts;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Features.Basket;

/// <summary>
/// Shared plumbing for the basket controllers: the mediator, the route prefix, and the pre-flight
/// that seven of the nine basket actions need — an anonymous guest's basket is keyed by the
/// <c>X-Session-Id</c> header, so without it there is nothing to act on. (The two promo-code stubs
/// accept the header and ignore it; they refuse unconditionally.)
/// </summary>
/// <remarks>
/// Extracted when <c>BasketController</c> was split (ORDER-TYPE-AVAILABILITY-PLAN §9.7): the same
/// four-line guard appeared in seven actions, and the split would have copied it into a second file.
/// <para>
/// The route prefix lives HERE rather than on each controller so the two cannot drift — with
/// <c>[Route("api/[controller]")]</c> on one and a literal on the other, renaming either class moves
/// half the prefix and leaves the other half behind.
/// </para>
/// <para>
/// The <c>"Session ID is required"</c> string is pinned by <c>BasketRoutingContractTests</c> as a
/// refactor guard, NOT because a client parses it: §9.4's rule is the opposite — this is one of the
/// 400s whose message must never reach a guest-facing toast, which is why the response carries no
/// <c>ErrorCode</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/Basket")]
public abstract class BasketControllerBase : ControllerBase
{
    protected const string SessionIdHeader = "X-Session-Id";

    protected CustomMediator Mediator { get; }

    protected BasketControllerBase(CustomMediator mediator)
    {
        Mediator = mediator;
    }

    /// <summary>
    /// The 400 to return when no session header arrived, or <c>null</c> to carry on. <c>[Pure]</c>
    /// so that discarding the result — which, unlike the inline <c>return BadRequest(...)</c> this
    /// replaced, silently removes the guard — is a build error rather than a missing check.
    /// </summary>
    /// <remarks>
    /// The type argument only shapes the (always-null) <c>data</c> slot, so every instantiation
    /// serialises identically; it is there to match the endpoint's success envelope, not because the
    /// bytes differ.
    /// </remarks>
    [Pure]
    protected ActionResult? MissingSession<T>(string sessionId) =>
        string.IsNullOrEmpty(sessionId)
            ? BadRequest(ApiResponse<T>.Failure("Session ID is required"))
            : null;
}
