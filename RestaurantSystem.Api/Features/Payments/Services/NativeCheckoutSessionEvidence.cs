using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;

namespace RestaurantSystem.Api.Features.Payments.Services;

/// <summary>Canonical provider identity is required before a native session can be recorded or shown.</summary>
public static class NativeCheckoutSessionEvidence
{
    public static void Require(CheckoutSessionRequest request, StripeCheckoutSession session)
    {
        var routing = request.NativeRouting
            ?? throw new InvalidOperationException("Native evidence requires frozen native routing.");
        routing.RequireReturn(request.OrderId);
        if (string.IsNullOrWhiteSpace(session.Id) || session.Id.Length > 255
            || !session.Id.StartsWith("cs_", StringComparison.Ordinal)
            || session.ProviderContext != routing.Context
            || session.Currency != request.Currency || session.AmountTotalMinor != request.AmountMinor
            || session.ClientReferenceId != request.OrderId.ToString("D")
            || session.Metadata.GetValueOrDefault(NativeCheckoutRouting.OperationMetadataKey) != routing.OperationId.ToString("D")
            || session.Metadata.GetValueOrDefault(NativeCheckoutRouting.SchemaMetadataKey) != NativeCheckoutRouting.SchemaVersion)
            throw new ConflictException("The original payment requires canonical provider reconciliation.");
    }
}
