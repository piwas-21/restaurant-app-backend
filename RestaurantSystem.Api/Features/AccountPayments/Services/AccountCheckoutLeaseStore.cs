using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>A bounded lease protects canonical provider I/O without holding database row locks.</summary>
public sealed class AccountCheckoutLeaseStore(ApplicationDbContext context,
    IOptions<AccountCheckoutSettings> options, TimeProvider clock) : IAccountCheckoutLeaseStore
{
    public async Task<AccountCheckoutJournal?> AcquireAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        RequireOwnTransaction();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var locked = await AccountCheckoutLocks.LoadAsync(context, attemptId, cancellationToken);
        var journal = locked.Journal;
        var now = clock.GetUtcNow().UtcDateTime;
        if (journal.LeaseId.HasValue && journal.LeaseExpiresAt > now)
            return null;
        if (locked.Attempt.State == AccountPaymentState.Captured && !journal.ReconciliationRequired
            && !journal.WebhookWakeupPending && journal.NextReconcileAt > now)
            return null;
        journal.LeaseId = Guid.NewGuid();
        journal.LeaseExpiresAt = now.AddSeconds(options.Value.ReconciliationLeaseSeconds);
        // A reconciliation that starts after the wakeup will read current provider truth itself.
        journal.WebhookWakeupPending = false;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return journal;
    }

    public async Task FinishAsync(Guid attemptId, Guid leaseId, string? failureCode,
        CancellationToken cancellationToken)
    {
        RequireOwnTransaction();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var locked = await AccountCheckoutLocks.LoadAsync(context, attemptId, cancellationToken);
        var journal = locked.Journal;
        if (journal.LeaseId != leaseId) return;
        journal.LeaseId = null;
        journal.LeaseExpiresAt = null;
        journal.LastFailureCode = failureCode;
        journal.ReconcileFailureCount = failureCode is null ? 0
            : Math.Min(int.MaxValue - 1, journal.ReconcileFailureCount) + 1;
        var now = clock.GetUtcNow().UtcDateTime;
        if (failureCode is null && journal.WebhookWakeupPending)
        {
            // An event arrived during provider I/O. Keep its durable marker for the next lease,
            // otherwise this finish could overwrite the wakeup with the settled fallback.
            journal.NextReconcileAt = now;
        }
        else if (failureCode is null && locked.Attempt.State == AccountPaymentState.Captured
            && !journal.ReconciliationRequired)
        {
            journal.NextReconcileAt = now.AddHours(options.Value.SettledReconciliationIntervalHours);
        }
        else if (failureCode is null)
        {
            journal.NextReconcileAt = now.AddSeconds(options.Value.ReconciliationIntervalSeconds);
        }
        else
        {
            var multiplier = Math.Pow(2, Math.Min(journal.ReconcileFailureCount, options.Value.MaximumBackoffExponent));
            var delay = Math.Min(options.Value.MaximumReconciliationBackoffSeconds,
                options.Value.ReconciliationIntervalSeconds * multiplier);
            journal.NextReconcileAt = now.AddSeconds(delay);
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ReadDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await context.AccountCheckoutJournals.AsNoTracking()
            .Join(context.AccountPaymentAttempts.AsNoTracking(), journal => journal.AttemptId,
                attempt => attempt.Id, (journal, attempt) => new { Journal = journal, attempt.State })
            .Where(value => value.Journal.NextReconcileAt <= now
                && (value.Journal.LeaseExpiresAt == null || value.Journal.LeaseExpiresAt <= now)
                && (value.State != AccountPaymentState.Released || value.Journal.ReconciliationRequired
                    || value.Journal.WebhookWakeupPending))
            .OrderBy(value => value.Journal.NextReconcileAt).ThenBy(value => value.Journal.Id)
            .Take(options.Value.ReconciliationBatchSize).Select(value => value.Journal.AttemptId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ScheduleWebhookWakeupAsync(AccountCheckoutWebhookReferences references,
        AccountStripeContext expectedContext, CancellationToken cancellationToken)
    {
        RequireOwnTransaction();
        if (!references.HasAny || string.IsNullOrWhiteSpace(expectedContext.ConnectedAccountId)) return false;

        var candidates = context.AccountCheckoutJournals.AsNoTracking()
            .Where(value => value.ProviderAccountId == expectedContext.ConnectedAccountId
                && value.ProviderLiveMode == expectedContext.LiveMode);
        if (references.AttemptId is Guid attemptId)
            candidates = candidates.Where(value => value.AttemptId == attemptId);
        else
            candidates = candidates.Where(value =>
                references.SessionId != null && value.ProviderSessionId == references.SessionId
                || references.IntentId != null && value.ProviderIntentId == references.IntentId
                || references.ChargeId != null && value.ProviderChargeId == references.ChargeId);

        var candidateIds = await candidates.Select(value => value.AttemptId).Take(2).ToListAsync(cancellationToken);
        if (candidateIds.Count != 1) return false;

        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var locked = await AccountCheckoutLocks.LoadAsync(context, candidateIds[0], cancellationToken);
        var journal = locked.Journal;
        if (journal.ProviderAccountId != expectedContext.ConnectedAccountId
            || journal.ProviderLiveMode != expectedContext.LiveMode
            || !ReferencesMatch(journal, references))
            return false;

        var now = clock.GetUtcNow().UtcDateTime;
        journal.WebhookWakeupPending = true;
        if (journal.NextReconcileAt > now) journal.NextReconcileAt = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool ReferencesMatch(AccountCheckoutJournal journal, AccountCheckoutWebhookReferences references)
    {
        if (references.AttemptId is Guid attemptId && attemptId != journal.AttemptId) return false;
        var matched = references.AttemptId == journal.AttemptId;
        if (references.SessionId is not null)
        {
            if (journal.ProviderSessionId is not null && journal.ProviderSessionId != references.SessionId) return false;
            matched |= journal.ProviderSessionId == references.SessionId;
        }
        if (references.IntentId is not null)
        {
            if (journal.ProviderIntentId is not null && journal.ProviderIntentId != references.IntentId) return false;
            matched |= journal.ProviderIntentId == references.IntentId;
        }
        if (references.ChargeId is not null)
        {
            if (journal.ProviderChargeId is not null && journal.ProviderChargeId != references.ChargeId) return false;
            matched |= journal.ProviderChargeId == references.ChargeId;
        }
        return matched;
    }

    private void RequireOwnTransaction()
    {
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Provider leasing must own its short transaction.");
    }
}
