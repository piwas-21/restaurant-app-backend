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
    /// placed the order is the one who needs to pay for it. The exposure that buys is narrow: the
    /// order id is required, nothing about the money is accepted from the caller, and the handler
    /// refuses an order that is closed or already paid. What is left — an attacker holding a
    /// scraped order id could mint a payment page for someone else's order, and see its total — is
    /// capped by the per-IP policy below, the same shape <c>send-confirmation-email</c> uses for
    /// the same reason.
    /// </remarks>
    [HttpPost("checkout-session")]
    [EnableRateLimiting("checkout-session")]
    public async Task<ActionResult<ApiResponse<CheckoutSessionDto>>> CreateCheckoutSession(
        [FromBody] CreateCheckoutSessionCommand command)
        => Ok(await _mediator.SendCommand(command));
}
