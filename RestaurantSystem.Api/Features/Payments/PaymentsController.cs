using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Payments.Commands.CreateCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Commands.SettleCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Dtos;
using RestaurantSystem.Api.Features.Payments.Queries.GetOnlinePaymentAvailabilityQuery;

namespace RestaurantSystem.Api.Features.Payments;

/// <summary>
/// Tenant → diner online payment (ADR-011 Job B). Its own controller rather than more routes on
/// <c>OrdersController</c>, which is at 144 of its 150 permitted lines.
/// </summary>
/// <remarks>
/// Gated at the CLASS level, unlike the order controllers: every route here exists only because the
/// tenant bought online payments, so there is no core surface to take away. That is also what
/// retires the <c>online-payments</c> exemption in <c>ModuleGateCoverageTests</c> — the module now
/// has a real gate, so it must satisfy "every paid module is enforced somewhere" like the others.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[RequireModule(ModuleIds.OnlinePayments)]
public class PaymentsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public PaymentsController(CustomMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Whether the checkout page may offer online payment at all (S8).
    /// </summary>
    /// <remarks>
    /// Anonymous for the same reason as <see cref="CreateCheckoutSession"/> below, and it discloses
    /// strictly less: one boolean about the restaurant, with no order id involved.
    ///
    /// <para>
    /// A caller that cannot reach this route must read that as UNAVAILABLE, never as unknown. Two
    /// non-answers are expected in normal operation and both mean "do not offer it": a tenant that
    /// did not buy the module gets the class gate's <b>404</b>, and a tenant still running a backend
    /// from before this slice gets a 404 because the route does not exist yet.
    /// </para>
    /// </remarks>
    [HttpGet("availability")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<OnlinePaymentAvailabilityDto>>> GetAvailability()
        => Ok(await _mediator.SendQuery(new GetOnlinePaymentAvailabilityQuery()));

    /// <summary>
    /// The diner's return trip from Stripe: settle the session, then say where that leaves the
    /// order (S9). **This is the PRIMARY settle trigger** — S7's reconciler is the backstop for
    /// the diner who closed the tab.
    /// </summary>
    /// <remarks>
    /// ANONYMOUS, like its siblings and for the same reason: a guest checkout has no account, and
    /// the person who just paid is the one who needs to be told it worked. It discloses an order
    /// NUMBER and two statuses to a holder of the session id — someone who, by holding it, could
    /// already read Stripe's own page for that payment.
    ///
    /// <para>
    /// <b>A GET that mutates, deliberately.</b> The settle is idempotent by construction (S5: the
    /// claim is a conditional UPDATE, and a non-<c>Created</c> session returns early without
    /// touching Stripe), so the usual hazard — a prefetch or a retried GET causing a second effect
    /// — cannot occur here. The frontend calls it explicitly; nothing prefetches it.
    /// </para>
    ///
    /// <para>
    /// <b>Rate-limited, but on its own generous policy.</b> The Stripe call is bounded per SETTLED
    /// session, not per session — a session Stripe still reports <c>open</c> is deliberately left
    /// at <c>Created</c> for the next sweep, so every call on it re-fetches from Stripe. Anyone can
    /// mint one session and then loop this route, which is an anonymous amplifier of reads against
    /// the tenant's connected account; Stripe answers a read flood with a <c>rate_limit</c> error,
    /// which is not <c>resource_missing</c> and so surfaces as a 500 — to real diners settling at
    /// the same time, and to S7's reconciler on the same key.
    /// </para>
    ///
    /// <para>
    /// So the limit exists to bound that, NOT to police diners, and it is sized accordingly: ~100x
    /// what the frontend asks for, which is one call per session id behind a ref guard with no
    /// polling. Its own partition, so a diner who spent their minting permits retrying can still be
    /// told whether the money arrived. A false 429 here is uniquely bad — it is shown to someone who
    /// has ALREADY PAID and says nothing about their money — and a shared restaurant Wi-Fi puts a
    /// whole room behind one partition key, so a bucket sized for one diner is sized wrong.
    /// </para>
    /// </remarks>
    [HttpGet("checkout-status")]
    [AllowAnonymous]
    [EnableRateLimiting("checkout-status")]
    public async Task<ActionResult<ApiResponse<CheckoutSettlementDto>>> GetCheckoutStatus(
        // Nullable on purpose: `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` is
        // set, so an absent `?sessionId=` binds to null with a VALID ModelState. Declaring it
        // non-nullable would state a contract the framework does not enforce; the validator does.
        [FromQuery] string? sessionId)
        => Ok(await _mediator.SendCommand(new SettleCheckoutSessionCommand { SessionId = sessionId ?? string.Empty }));

    /// <summary>
    /// Mints (or re-hands out) the Stripe hosted-Checkout page for an order.
    /// </summary>
    /// <remarks>
    /// ANONYMOUS by necessity — guest checkout has no account (ADR-004), and the diner who just
    /// placed the order is the one who needs to pay for it. Nothing about the money is accepted
    /// from the caller, and the handler refuses an order that is closed or already (part-)paid.
    ///
    /// <para>
    /// What is left, stated plainly: an attacker holding a scraped order id can mint a Stripe page
    /// for someone else's open order and read its ORDER NUMBER and TOTAL from it. The diner's
    /// email is deliberately not prefilled so it is not also on that page. The per-IP policy below
    /// raises the cost — the same shape <c>send-confirmation-email</c> uses — but it is not a hard
    /// cap: <c>ForwardedHeaders</c> is configured to trust any upstream, so <c>X-Forwarded-For</c>
    /// is caller-controlled and the partition key with it. That is a pre-existing fleet-wide
    /// property, not something this endpoint introduced, and it is why the disclosure above is
    /// kept to fields Stripe's own page shows anyway.
    /// </para>
    /// </remarks>
    [HttpPost("checkout-session")]
    // Explicit, though `AddAuthorization()` registers no fallback policy today so an unmarked
    // endpoint is already anonymous. Stating it matches the sibling guest endpoint
    // (OrderEmailController's send-confirmation-email) and, more to the point, means a future
    // fallback policy cannot silently 401 the diners this exists to serve.
    [AllowAnonymous]
    [EnableRateLimiting("checkout-session")]
    public async Task<ActionResult<ApiResponse<CheckoutSessionDto>>> CreateCheckoutSession(
        [FromBody] CreateCheckoutSessionCommand command)
        => Ok(await _mediator.SendCommand(command));
}
