using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Unknown, incomplete and conflicting provider evidence always retain the debt reservation.</summary>
public static class AccountStripeEvidence
{
    public static void RequireSession(AccountStripeExpectation expected, AccountStripeSession session)
    {
        RequireMetadata(expected, session.Context, session.Metadata);
        if (string.IsNullOrWhiteSpace(session.Id)
            || expected.SessionId is not null && expected.SessionId != session.Id
            || session.ClientReferenceId != expected.AttemptId.ToString("D")
            || session.AmountMinor != expected.AmountMinor || !string.Equals(session.Currency, expected.Currency, StringComparison.OrdinalIgnoreCase)
            || expected.IntentId is not null && expected.IntentId != session.IntentId)
            throw new ConflictException("The checkout does not match the original contribution. Reconciliation is required.");
    }

    public static void RequireIntent(
        AccountStripeExpectation expected, AccountStripeSession session, AccountStripeIntent intent)
    {
        RequireSession(expected, session);
        RequireMetadata(expected, intent.Context, intent.Metadata);
        if (string.IsNullOrWhiteSpace(session.IntentId) || session.IntentId != intent.Id
            || expected.IntentId is not null && expected.IntentId != intent.Id
            || intent.AmountMinor != expected.AmountMinor || !string.Equals(intent.Currency, expected.Currency, StringComparison.OrdinalIgnoreCase)
            || intent.ReceivedMinor < 0 || intent.ReceivedMinor > expected.AmountMinor)
            throw new ConflictException("The payment does not match the original contribution. Reconciliation is required.");
    }

    public static bool HasCaptured(
        AccountStripeExpectation expected, AccountStripeSession session, AccountStripeIntent intent)
    {
        RequireIntent(expected, session, intent);
        return intent.Status == "succeeded" && intent.ReceivedMinor == expected.AmountMinor
            && !string.IsNullOrWhiteSpace(intent.ChargeId);
    }

    public static bool CanRelease(
        AccountStripeExpectation expected, AccountStripeSession session, AccountStripeIntent? intent)
    {
        RequireSession(expected, session);
        if (session.Status != "expired" || session.PaymentStatus != "unpaid") return false;
        if (session.IntentId is null) return intent is null;
        if (intent is null) return false;
        RequireIntent(expected, session, intent);
        // A failed payment method can still be retried. Only canonical cancellation ends that possibility.
        return intent.Status == "canceled" && intent.ReceivedMinor == 0;
    }

    private static void RequireMetadata(
        AccountStripeExpectation expected, AccountStripeContext context, IReadOnlyDictionary<string, string> metadata)
    {
        if (expected.AttemptId == Guid.Empty || expected.AmountMinor <= 0
            || string.IsNullOrWhiteSpace(expected.Context.ConnectedAccountId)
            || expected.Context != context
            || !metadata.TryGetValue(AccountStripeCheckoutClient.AttemptMetadataKey, out var attemptId)
            || attemptId != expected.AttemptId.ToString("D")
            || !metadata.TryGetValue(AccountStripeCheckoutClient.SchemaMetadataKey, out var schema)
            || schema != AccountStripeCheckoutClient.SchemaVersion)
            throw new ConflictException("The provider identity does not match the contribution. Reconciliation is required.");
    }
}
