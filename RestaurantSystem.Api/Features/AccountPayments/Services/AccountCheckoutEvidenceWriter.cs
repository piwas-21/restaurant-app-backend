using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Durable canonical money evidence survives a later allocation-posting failure.</summary>
public sealed class AccountCheckoutEvidenceWriter(ApplicationDbContext context,
    IOptions<AccountCheckoutSettings> options, TimeProvider clock) : IAccountCheckoutEvidenceWriter
{
    public async Task<AccountCheckoutJournal> RecordAsync(AccountCheckoutJournal original,
        AccountCheckoutCanonicalEvidence evidence, CancellationToken cancellationToken)
    {
        RequireOwnTransaction();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var locked = await AccountCheckoutLocks.LoadAsync(context, original.AttemptId, cancellationToken);
        var journal = locked.Journal;
        if (journal.CreatePayloadHash != original.CreatePayloadHash)
            throw new ConflictException("The frozen checkout request changed. Reconciliation is required.");
        if (original.LeaseId is null || journal.LeaseId != original.LeaseId
            || journal.LeaseExpiresAt is not DateTime leaseExpires
            || leaseExpires <= clock.GetUtcNow().UtcDateTime)
            throw new ConflictException("Another recovery owns this provider contribution.");
        var before = (locked.Attempt.State, journal.ProviderCapturedMinor,
            journal.ProviderRefundedMinor, journal.ReconciliationRequired);
        evidence.Validate(journal);
        var verifiedRefundedMinor = await AccountCheckoutAmendmentRefundProof.ValidateAsync(
            context, journal, evidence, cancellationToken);
        journal.ProviderSessionId = evidence.Session.Id;
        journal.ProviderIntentId = evidence.Session.IntentId;
        locked.Attempt.ProviderSessionId = evidence.Session.Id;
        locked.Attempt.ProviderAccountId = journal.ProviderAccountId;
        RecordCharge(locked, evidence);
        RecordState(locked, evidence, verifiedRefundedMinor);
        // Preserve the evidence identity across PostgreSQL timestamp precision.
        journal.LastVerifiedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()).UtcDateTime;
        journal.NextReconcileAt = journal.LastVerifiedAt.Value.AddSeconds(options.Value.ReconciliationIntervalSeconds);
        journal.LastFailureCode = null;
        journal.ReconcileFailureCount = 0;
        if (before != (locked.Attempt.State, journal.ProviderCapturedMinor,
                journal.ProviderRefundedMinor, journal.ReconciliationRequired))
            locked.Session.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return journal;
    }

    private void RecordState(LockedAccountCheckout locked, AccountCheckoutCanonicalEvidence evidence,
        long verifiedRefundedMinor)
    {
        var attempt = locked.Attempt;
        var journal = locked.Journal;
        if (journal.ProviderCapturedMinor > 0)
        {
            var hasCapture = evidence.HasCapturedChargeWithVerifiedRefunds(journal, verifiedRefundedMinor);
            var posted = attempt.State == AccountPaymentState.Captured
                && attempt.Allocations.Count > 0 && attempt.Allocations.All(value => value.OrderPaymentId.HasValue);
            journal.ReconciliationRequired = !hasCapture || !posted;
            if (!posted) ChangeState(attempt, hasCapture && verifiedRefundedMinor == 0
                ? AccountPaymentState.Processing : AccountPaymentState.ReconciliationRequired);
            return;
        }
        if (evidence.CanRelease(journal) && attempt.State != AccountPaymentState.Captured
            && attempt.Allocations.All(value => value.OrderPaymentId is null))
        {
            ChangeState(attempt, AccountPaymentState.Released);
            attempt.CompletedAt = clock.GetUtcNow().UtcDateTime;
            journal.ReconciliationRequired = false;
            return;
        }
        if (attempt.State == AccountPaymentState.Starting)
            ChangeState(attempt, AccountPaymentState.Processing);
    }

    private static void RecordCharge(LockedAccountCheckout locked, AccountCheckoutCanonicalEvidence evidence)
    {
        var charge = evidence.Charge;
        // A failed card attempt can be replaced by a later Charge on the same PaymentIntent.
        // Freeze charge identity only when canonical evidence proves captured funds.
        if (charge is null || charge.CapturedMinor == 0 && locked.Journal.ProviderCapturedMinor == 0) return;
        var journal = locked.Journal;
        if (journal.ProviderChargeId is not null && journal.ProviderChargeId != charge.Id
            || charge.CapturedMinor < journal.ProviderCapturedMinor
            || charge.RefundedMinor < journal.ProviderRefundedMinor)
            throw new ConflictException("Canonical charge evidence conflicts with already verified money.");
        journal.ProviderChargeId = charge.Id;
        locked.Attempt.ProviderChargeId = charge.Id;
        journal.ProviderCapturedMinor = charge.CapturedMinor;
        journal.ProviderRefundedMinor = charge.RefundedMinor;
    }

    private void ChangeState(AccountPaymentAttempt attempt, AccountPaymentState state)
    {
        if (attempt.State == state) return;
        attempt.State = state;
        attempt.Version++;
        attempt.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        attempt.UpdatedBy = attempt.CreatedBy;
    }

    private void RequireOwnTransaction()
    {
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Provider evidence must commit its own short transaction.");
    }
}
