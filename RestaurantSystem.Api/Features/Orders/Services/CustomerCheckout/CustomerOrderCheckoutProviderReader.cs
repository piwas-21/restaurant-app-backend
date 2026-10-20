using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerOrderCheckoutProviderReader(IStripeCheckoutClient provider, IStripeGateway gateway,
    IOptions<StripeSettings> stripe) : ICustomerOrderCheckoutProviderReader
{
    public async Task<StripeCheckoutSession> ReadAsync(CheckoutSessionRequest request, string sessionId,
        CancellationToken cancellationToken)
    {
        var routing = request.NativeRouting
            ?? throw new InvalidOperationException("Native recovery requires frozen provider routing.");
        routing.RequireCurrent(gateway, stripe.Value, request.OrderId);
        var session = await provider.GetAsync(sessionId, cancellationToken) ?? throw Pending();
        NativeCheckoutSessionEvidence.Require(request, session);
        if (session.Id != sessionId) throw Pending();
        if ((session.IsComplete || session.IsExpired) && session.PaymentIntentId is { } id)
        {
            routing.RequireCurrent(gateway, stripe.Value, request.OrderId);
            var intent = await provider.GetPaymentIntentAsync(id, cancellationToken) ?? throw Pending();
            NativeCheckoutIntentEvidence.Require(request, session, intent);
            return session with { NativeIntentEvidence = intent };
        }
        return session;
    }

    private static ConflictException Pending() => new("The original payment requires provider reconciliation.");
}
