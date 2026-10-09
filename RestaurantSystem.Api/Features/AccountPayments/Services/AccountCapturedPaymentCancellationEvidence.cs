using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Checks frozen capture evidence for only the captured scopes being cancelled.</summary>
internal static class AccountCapturedPaymentCancellationEvidence
{
    internal static async Task ValidateAsync(ApplicationDbContext context,
        IReadOnlyList<AccountPaymentAttempt> attempts, AccountMoney money,
        CancellationToken cancellationToken)
    {
        var cashAttempts = attempts.Where(value => value.PaymentMethod == PaymentMethod.Cash).ToArray();
        ValidateCashSnapshots(cashAttempts);
        var onlineAttempts = attempts.Where(value => value.PaymentMethod == PaymentMethod.OnlinePayment).ToArray();
        if (onlineAttempts.Length == 0)
            return;

        var attemptIds = onlineAttempts.Select(value => value.Id).ToArray();
        var journals = await context.AccountCheckoutJournals.AsNoTracking()
            .Where(value => attemptIds.Contains(value.AttemptId)).ToArrayAsync(cancellationToken);
        foreach (var attempt in onlineAttempts)
        {
            var matching = journals.Where(value => value.AttemptId == attempt.Id).ToArray();
            if (matching.Length != 1)
                throw ReconciliationRequired();
            ValidateOnlineCapture(attempt, matching[0], money);
        }
    }

    private static void ValidateCashSnapshots(IReadOnlyList<AccountPaymentAttempt> attempts)
    {
        foreach (var attempt in attempts)
        {
            AccountPaymentQuoteSnapshot snapshot;
            try
            {
                snapshot = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
            }
            catch (JsonException exception)
            {
                throw new ConflictException("The captured cash quote requires reconciliation.", exception);
            }
            AccountCashCaptureReceiptPolicy.ValidateStored(attempt, snapshot);
        }
    }

    private static void ValidateOnlineCapture(
        AccountPaymentAttempt attempt, AccountCheckoutJournal journal, AccountMoney money)
    {
        var payments = attempt.Allocations.Select(value => value.OrderPayment).ToArray();
        if (attempt.State != AccountPaymentState.Captured || attempt.PaymentMethod != PaymentMethod.OnlinePayment
            || attempt.AmountMinor <= 0 || attempt.Currency != money.Currency
            || journal.AttemptId != attempt.Id || journal.StartedAttemptVersion <= 0
            || attempt.Version <= journal.StartedAttemptVersion
            || journal.AmountMinor != attempt.AmountMinor || journal.Currency != attempt.Currency
            || journal.ReconciliationRequired || journal.ProviderCapturedMinor != attempt.AmountMinor
            || journal.ProviderRefundedMinor < 0 || journal.ProviderRefundedMinor > journal.ProviderCapturedMinor
            || journal.LastVerifiedAt is null
            || journal.ExpiresAt <= journal.StartedAt || journal.MaximumCreateRetryAt <= journal.StartedAt
            || journal.MaximumCreateRetryAt > journal.StartedAt.AddHours(23)
            || journal.CreateIdempotencyKey != AccountCheckoutReplayPayload.CreateKey(attempt.Id)
            || string.IsNullOrWhiteSpace(journal.ProviderAccountId)
            || !journal.ProviderAccountId.StartsWith("acct_", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(journal.ProviderSessionId)
            || !journal.ProviderSessionId.StartsWith("cs_", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(journal.ProviderIntentId)
            || !journal.ProviderIntentId.StartsWith("pi_", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(journal.ProviderChargeId)
            || !journal.ProviderChargeId.StartsWith("ch_", StringComparison.Ordinal)
            || attempt.ProviderAccountId != journal.ProviderAccountId
            || attempt.ProviderSessionId != journal.ProviderSessionId
            || attempt.ProviderChargeId != journal.ProviderChargeId
            || payments.Length == 0 || payments.Any(payment => payment is null
                || !AccountCheckoutEvidenceGuard.HasProviderCaptureIdentity(attempt, payment)))
            throw ReconciliationRequired();

        var request = new AccountStripeCheckoutRequest
        {
            AttemptId = attempt.Id,
            Context = new AccountStripeContext(journal.ProviderAccountId, journal.ProviderLiveMode),
            AmountMinor = journal.AmountMinor,
            Currency = journal.Currency,
            ExpiresAt = journal.ExpiresAt,
            IdempotencyKey = journal.CreateIdempotencyKey,
            ReturnBaseUrl = journal.ReturnBaseUrl
        };
        if (!string.Equals(AccountCheckoutReplayPayload.Hash(request), journal.CreatePayloadHash,
                StringComparison.Ordinal))
            throw ReconciliationRequired();
    }

    private static ConflictException ReconciliationRequired() => new(
        "The captured account payment evidence requires reconciliation before cancellation.");
}
