using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>PaymentIntent success alone cannot prove current net funds after a refund or dispute.</summary>
public static class AccountStripeChargeEvidence
{
    public static void RequireCharge(
        AccountStripeExpectation expected, AccountStripeSession session,
        AccountStripeIntent intent, AccountStripeCharge charge)
    {
        AccountStripeEvidence.RequireIntent(expected, session, intent);
        if (string.IsNullOrWhiteSpace(intent.ChargeId) || intent.ChargeId != charge.Id
            || charge.IntentId != intent.Id || charge.Context != expected.Context
            || charge.AmountMinor != expected.AmountMinor
            || !string.Equals(charge.Currency, expected.Currency, StringComparison.OrdinalIgnoreCase)
            || charge.CapturedMinor < 0 || charge.CapturedMinor > charge.AmountMinor
            || charge.RefundedMinor < 0 || charge.RefundedMinor > charge.CapturedMinor)
            throw new ConflictException("The charge does not match the original contribution. Reconciliation is required.");
    }

    public static bool HasUnreversedCapture(
        AccountStripeExpectation expected, AccountStripeSession session,
        AccountStripeIntent intent, AccountStripeCharge charge)
    {
        RequireCharge(expected, session, intent, charge);
        return AccountStripeEvidence.HasCaptured(expected, session, intent)
            && charge.Status == "succeeded" && charge.Paid && charge.Captured
            && charge.CapturedMinor == expected.AmountMinor
            && charge.RefundedMinor == 0 && !charge.Disputed;
    }
}
