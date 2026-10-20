using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Tenant-scoped server authority for known financial references, including after owner erasure.</summary>
public sealed class CustomerOrderCheckoutReconciler(ApplicationDbContext context,
    ICustomerOrderCheckoutEvidenceWriter writer, ICustomerOrderCheckoutProviderReader reader,
    IOptions<CustomerHostedCheckoutSettings> settings,
    TimeProvider clock, ICustomerOrderCheckoutEffects effects,
    ILogger<CustomerOrderCheckoutReconciler> logger) : ICustomerOrderCheckoutReconciler
{
    private const int MaximumBatchSize = 100;

    public async Task<CheckoutExpirySweepReport> RunAsync(int batchSize, CancellationToken cancellationToken)
    {
        RequireClean();
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking()
            .Where(value => value.ProviderSessionId != null && (value.LeaseExpiresAt == null || value.LeaseExpiresAt <= now)
                && value.EffectsCompletedAt == null
                && !context.OrderCheckoutSessions.Any(row => row.OrderId == value.OrderId
                    && (row.Status == CheckoutSessionStatus.Expired || row.Status == CheckoutSessionStatus.Failed)))
            // Rotate both verified-pending and failed work; one poison batch cannot starve newer sessions.
            .OrderBy(value => value.LeaseExpiresAt ?? value.LastVerifiedAt ?? value.StartedAt).ThenBy(value => value.OrderId)
            .Take(Math.Clamp(batchSize, 1, MaximumBatchSize)).Select(value => value.OrderId).ToArrayAsync(cancellationToken);
        var settled = 0;
        var expired = 0;
        var failures = 0;
        var examined = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.Value.ReconciliationBudgetSeconds,
            1, CustomerHostedCheckoutSettings.MaximumReconciliationBudgetSeconds)));
        var workToken = budget.Token;
        foreach (var orderId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (workToken.IsCancellationRequested) break;
            examined++;
            try
            {
                var result = await ReconcileAsync(orderId, workToken);
                if (result.Outcome == CheckoutSessionStatus.Completed) settled++;
                if (result.Outcome == CheckoutSessionStatus.Expired) expired++;
                if (result.Effects) await effects.CompleteAsync(orderId, workToken);
            }
            catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                failures++;
                ReportFailure(orderId, error);
            }
        }
        return new() { Examined = examined, Settled = settled, Expired = expired, Failures = failures };
    }

    private async Task<(CheckoutSessionStatus? Outcome, bool Effects)> ReconcileAsync(Guid orderId,
        CancellationToken cancellationToken)
    {
        var work = await AcquireAsync(orderId, cancellationToken);
        if (work is null) return (null, false);
        if (work.Payment is not { } lease) return (null, true);
        if (clock.GetUtcNow().UtcDateTime >= lease.LeaseExpiresAt) throw Pending();
        // The reader verifies frozen routing immediately before provider I/O, outside any transaction.
        var remote = await reader.ReadAsync(lease.Request, lease.ProviderSessionId!, cancellationToken);
        var outcome = await RecordAsync(lease, remote, cancellationToken);
        return (outcome, outcome == CheckoutSessionStatus.Completed);
    }

    private void ReportFailure(Guid orderId, Exception error)
    {
        // Provider exception text may contain URLs, credentials or customer data. Retain only classification.
        logger.LogWarning("Native payment requires reconciliation for order {OrderId}; failure type {FailureType}",
            orderId, error.GetType().Name);
    }

    private sealed record Work(CustomerOrderCheckoutLease? Payment);

    private async Task<Work?> AcquireAsync(Guid orderId, CancellationToken cancellationToken)
    {
        RequireClean();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using var orderLock = await OrderAccountMutationScope.BeginAsync(context, orderId, cancellationToken);
        var status = await context.OrderCheckoutSessions.Where(row => row.OrderId == orderId)
            .Select(row => (CheckoutSessionStatus?)row.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is CheckoutSessionStatus.Expired or CheckoutSessionStatus.Failed) return null;
        var journal = await ReadAsync(orderId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (journal.LeaseExpiresAt > now || journal.EffectsCompletedAt is not null) return null;
        var id = Guid.NewGuid();
        var expiry = now.Add(CustomerOrderCheckoutLeaseStore.LeaseLifetime);
        await context.Set<CustomerOrderCheckoutJournal>().Where(value => value.OrderId == orderId)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.LeaseId, id)
                .SetProperty(value => value.LeaseExpiresAt, expiry), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Commit rotation before validating retained/provider inputs: poisoned rows must back off too.
        if (status == CheckoutSessionStatus.Completed) return new(null);
        var request = CustomerOrderCheckoutReplay.Read(journal, now, forCreate: false);
        return new(new(id, request, journal.ProviderSessionId, expiry, journal.MaximumCreateRetryAt));
    }

    private async Task<CheckoutSessionStatus?> RecordAsync(CustomerOrderCheckoutLease lease, StripeCheckoutSession remote,
        CancellationToken cancellationToken)
    {
        RequireClean();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using var orderLock = await OrderAccountMutationScope.BeginAsync(context, lease.Request.OrderId, cancellationToken);
        var journal = await ReadAsync(lease.Request.OrderId, cancellationToken);
        var result = await writer.WriteAsync(journal, lease, remote, clock.GetUtcNow().UtcDateTime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private Task<CustomerOrderCheckoutJournal> ReadAsync(Guid orderId, CancellationToken cancellationToken) =>
        context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync(value => value.OrderId == orderId
            && value.ProviderSessionId != null, cancellationToken);

    private void RequireClean()
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Native reconciliation requires isolated clean phases.");
    }

    private static ConflictException Pending() => new("The original payment requires canonical reconciliation.");
}
