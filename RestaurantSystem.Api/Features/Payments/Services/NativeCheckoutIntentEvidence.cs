using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Payments.Services;

public static class NativeCheckoutIntentEvidence
{
    public static void Require(CheckoutSessionRequest request, StripeCheckoutSession session, StripePaymentIntent intent)
    {
        NativeCheckoutSessionEvidence.Require(request, session);
        var routing = request.NativeRouting!;
        // The canonical session binds its client-reference order and this exact intent ID;
        // native operation/schema metadata independently binds both provider objects to the request.
        if (intent.Id != session.PaymentIntentId || !intent.Id.StartsWith("pi_", StringComparison.Ordinal)
            || intent.Id.Length is <= 3 or > 255 || intent.Id.Any(char.IsWhiteSpace)
            || intent.ProviderContext != routing.Context || intent.Currency != request.Currency
            || intent.AmountMinor != request.AmountMinor || intent.ApplicationFeeMinor != request.ApplicationFeeMinor
            || intent.AmountReceivedMinor is null or < 0
            || intent.AmountReceivedMinor > request.AmountMinor
            || intent.Metadata.GetValueOrDefault(NativeCheckoutRouting.OperationMetadataKey) != routing.OperationId.ToString("D")
            || intent.Metadata.GetValueOrDefault(NativeCheckoutRouting.SchemaMetadataKey) != NativeCheckoutRouting.SchemaVersion)
            throw new ConflictException("The original payment intent needs canonical reconciliation.");
    }
}
