using System.Globalization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using Stripe.Checkout;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <inheritdoc />
public class StripeCheckoutClient : IStripeCheckoutClient
{
    /// <summary>
    /// Stripe substitutes the real session id into the success URL. Literal because it is Stripe's
    /// wire token, not a value of ours — templating it would only hide that.
    /// </summary>
    private const string StripeSessionIdPlaceholder = "{CHECKOUT_SESSION_ID}";

    private readonly IStripeGateway _gateway;
    private readonly StripeSettings _stripe;
    private readonly EmailSettings _email;

    public StripeCheckoutClient(
        IStripeGateway gateway,
        IOptions<StripeSettings> stripe,
        IOptions<EmailSettings> email)
    {
        ArgumentNullException.ThrowIfNull(stripe);
        ArgumentNullException.ThrowIfNull(email);

        _gateway = gateway;
        _stripe = stripe.Value;
        _email = email.Value;
    }

    public async Task<StripeCheckoutSession> CreateAsync(
        CheckoutSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = new SessionCreateOptions
        {
            Mode = "payment",

            // Deliberately NOT set: PaymentMethodTypes. Leaving it off is what turns on Stripe's
            // dynamic payment methods, which selects per session from merchant country + currency +
            // customer location — measured to be what makes a French account offer iDEAL/Bancontact
            // and a Swiss one offer TWINT with zero per-country code on our side (plan §3).
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency,
                        UnitAmount = request.AmountMinor,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            // ONE line for the whole order, not a line per item. The order is
                            // already priced and persisted server-side; re-sending a basket to
                            // Stripe would create a second place for the total to be computed,
                            // and the two could disagree.
                            Name = string.Format(
                                CultureInfo.InvariantCulture, "Order {0}", request.OrderNumber),
                        },
                    },
                },
            ],

            // ≤200 chars, honoured on connected accounts (measured). This is how a Stripe-side
            // support question maps back to an order without a lookup table.
            ClientReferenceId = request.OrderId.ToString(),

            CustomerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail,

            ExpiresAt = request.ExpiresAt,

            // The success trip carries the session id — it is what S9 settles on. The cancel trip
            // carries only the order id: Stripe substitutes the placeholder on success ONLY, so
            // asking for it on the cancel URL would return the literal `{CHECKOUT_SESSION_ID}`.
            SuccessUrl = BuildReturnUrl(
                _stripe.SuccessPath, request.OrderId, $"sessionId={StripeSessionIdPlaceholder}"),
            CancelUrl = BuildReturnUrl(_stripe.CancelPath, request.OrderId, "canceled=1"),
        };

        var session = await new SessionService(_gateway.Client)
            .CreateAsync(options, _gateway.BuildRequestOptions(request.IdempotencyKey), cancellationToken);

        return Map(session);
    }

    public async Task<StripeCheckoutSession?> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await new SessionService(_gateway.Client)
            .GetAsync(sessionId, options: null, _gateway.BuildRequestOptions(), cancellationToken);

        return session is null ? null : Map(session);
    }

    private static StripeCheckoutSession Map(Session session) => new()
    {
        Id = session.Id,
        Url = session.Url,
        Status = session.Status ?? string.Empty,
        PaymentStatus = session.PaymentStatus ?? string.Empty,
        // PaymentIntentId is populated on the session itself; the expanded PaymentIntent object is
        // only present when explicitly requested, and we never need more than the id.
        PaymentIntentId = session.PaymentIntentId,
        AmountTotalMinor = session.AmountTotal,
    };

    /// <summary>
    /// Return URLs are composed from the validated <c>EmailSettings.FrontendBaseUrl</c> plus a
    /// configured PATH, so the diner can only ever be returned to this tenant's own origin.
    /// </summary>
    private string BuildReturnUrl(string path, Guid orderId, string extra)
    {
        var baseUrl = _email.FrontendBaseUrl.TrimEnd('/');
        var suffix = path.StartsWith('/') ? path : "/" + path;

        return $"{baseUrl}{suffix}?orderId={orderId}&{extra}";
    }
}
