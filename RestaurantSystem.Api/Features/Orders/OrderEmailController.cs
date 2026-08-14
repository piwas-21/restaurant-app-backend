using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrderByIdQuery;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders;

// Email-confirmation endpoint split out from OrdersController in
// Sprint 2 task 2.4. Composition of the email body now lives in
// IOrderNotificationService (task 2.10); this controller is a thin
// dispatcher.
[ApiController]
[Route("api/orders")]
public class OrderEmailController : ControllerBase
{
    private readonly CustomMediator _mediator;
    private readonly IOrderNotificationService _notifications;
    private readonly ILogger<OrderEmailController> _logger;

    public OrderEmailController(
        CustomMediator mediator,
        IOrderNotificationService notifications,
        ILogger<OrderEmailController> logger)
    {
        _mediator = mediator;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Resends the order confirmation emails to customer and admin, if they have not gone out yet.
    /// </summary>
    /// <remarks>
    /// <para><b>This is no longer how an order's mail gets sent.</b> Since GAP-11
    /// (EMAIL-SPEC-TENANT-APP §4) <c>CreateOrderCommandHandler</c> sends both mails the moment the
    /// order commits, because a guest who closed the tab used to cost the restaurant its only
    /// email notice of a real order. The endpoint stays for the clients that still call it — an
    /// older frontend image on a tenant box, a support-triggered resend — and is now a no-op
    /// whenever the mail already went out: <c>IOutboundEmailLedger</c> claims each send, so a
    /// replay, or a client call racing the server's own, cannot produce a second mail (GAP-12).</para>
    /// <para><b>Auth posture: intentionally [AllowAnonymous]</b>. See ADR-004.</para>
    /// <para>
    /// Called from the checkout review page (<c>frontend/src/app/checkout/review/page.tsx</c>)
    /// immediately after order creation. Guest checkout is supported — the
    /// caller has no bearer token at that point — so requiring auth here would
    /// break the takeaway/delivery confirmation flow for unauthenticated
    /// customers (dine-in confirmations are sent synchronously from
    /// <c>CreateOrderCommandHandler</c> and bypass this endpoint).
    /// </para>
    /// <para><b>Threat surface:</b></para>
    /// <list type="bullet">
    ///   <item><description>
    ///     An attacker who scrapes order IDs (URLs, receipt PDFs) could replay
    ///     this endpoint to spam the legitimate customer's inbox and to flood
    ///     the admin notification mailbox (inflating SMTP cost).
    ///   </description></item>
    ///   <item><description>
    ///     No order data is returned in the HTTP response body — the order
    ///     details are only delivered to the addresses already recorded on
    ///     the order, so this is not an enumeration leak; it is an SMTP-cost
    ///     and customer-spam abuse vector.
    ///   </description></item>
    ///   <item><description>
    ///     Guessing a valid GUID is infeasible (128-bit space). The realistic
    ///     attack is replay against a known order ID.
    ///   </description></item>
    /// </list>
    /// <para><b>Mitigation:</b> the send-once ledger above caps the abuse at zero extra mails per
    /// order, and behind it a per-IP fixed-window rate limit
    /// (<c>"confirmation-email"</c> policy, see <c>Program.cs</c>). Defaults
    /// to 5 requests / 15 minutes / IP in production. Tune via
    /// <c>RateLimiter:ConfirmationEmail*</c> in <c>appsettings.json</c>.</para>
    /// </remarks>
    [HttpPost("{orderId}/send-confirmation-email")]
    [AllowAnonymous]
    [EnableRateLimiting("confirmation-email")]
    public async Task<ActionResult<ApiResponse<string>>> SendOrderConfirmationEmail(Guid orderId)
    {
        // EnforceOwnership: false — this endpoint is [AllowAnonymous] by design (above), so the
        // caller is routinely a guest with no token and the order routinely has UserId == null.
        // The ownership check would reject both. Safe here because the order is never returned
        // to the caller: it only feeds the email, which goes to the addresses already recorded
        // on the order. The response body carries no order data either way.
        var orderResult = await _mediator.SendQuery(new GetOrderByIdQuery(orderId, EnforceOwnership: false));
        if (!orderResult.Success || orderResult.Data == null)
        {
            return BadRequest(ApiResponse<string>.Failure("Order not found"));
        }

        try
        {
            await _notifications.SendOrderConfirmationAsync(orderResult.Data);
            return Ok(ApiResponse<string>.SuccessWithData("Order confirmation emails sent successfully"));
        }
        catch (Exception ex)
        {
            // Generic message — `ex.Message` can leak SMTP server details.
            // Full exception logged server-side; the client gets a stable
            // surface. Closes part of issue #13.
            _logger.LogError(ex, "Failed to send order confirmation emails for order {OrderId}", orderId);
            return BadRequest(ApiResponse<string>.Failure("Failed to send confirmation emails"));
        }
    }
}
