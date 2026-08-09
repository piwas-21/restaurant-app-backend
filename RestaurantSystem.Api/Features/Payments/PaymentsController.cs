using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Payments.Commands.CreateCheckoutSessionCommand;
using RestaurantSystem.Api.Features.Payments.Dtos;

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
