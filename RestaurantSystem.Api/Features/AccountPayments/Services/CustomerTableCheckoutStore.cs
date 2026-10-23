using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Original-proof money decisions commit before the shared provider reconciler runs.</summary>
public sealed class CustomerTableCheckoutStore(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ICustomerTablePaymentProofs proofs, ICustomerTablePaymentDecisions decisions,
    ICustomerTableCheckoutFreezer freezer, TimeProvider clock) : ICustomerTableCheckoutStore
{
    public async Task<CustomerTableCheckoutIdentity> RunAsync(string sessionId, CustomerTablePaymentProofDto proof,
        string? participantToken, CustomerTableCheckoutAction action, CancellationToken cancellationToken)
    {
        if (proof is null) throw new BadRequestException("The original contribution proof is required.");
        var owner = access.Capture(sessionId, proof.SchemaVersion, proof.TenantId);
        var binding = proofs.Require(proof, owner);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new ConflictException("Native checkout requires an isolated clean context.");
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var visit = await TableServiceSessionRowLock.LoadAsync(context, binding.ServiceSessionId, cancellationToken);
            if (owner.CustomerId is { } customer)
                await CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken);
            var decision = await decisions.ReadAsync(owner, proof, binding, cancellationToken)
                ?? throw Unavailable();
            var original = new CustomerTableCheckoutIdentity(owner, binding, decision.AttemptId, false);
            var hasJournal = await RunAttemptAsync(visit, original, proof, participantToken, action, cancellationToken);
            if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != owner) throw Unavailable();
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(owner, binding, decision.AttemptId, hasJournal);
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    private async Task<bool> RunAttemptAsync(TableServiceSession? visit, CustomerTableCheckoutIdentity original,
        CustomerTablePaymentProofDto proof, string? participantToken, CustomerTableCheckoutAction action,
        CancellationToken cancellationToken)
    {
        if (original.AttemptId is null) return false;
        if (visit is null) throw Unavailable();
        var attempt = await context.AccountPaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM account_payment_attempts WHERE id = {original.AttemptId.Value} FOR UPDATE")
            .Include(row => row.Allocations).SingleAsync(cancellationToken);
        var journal = await context.AccountCheckoutJournals
            .FromSqlInterpolated($"SELECT * FROM account_checkout_journals WHERE attempt_id = {original.AttemptId.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (journal is not null)
        {
            RequireJournal(journal, proof);
            if (action == CustomerTableCheckoutAction.Stop) RequestStop(attempt, journal);
            return true;
        }
        else if (attempt.StartedAt is not null || attempt.ProviderSessionId is not null
            || attempt.ProviderChargeId is not null || attempt.ProviderAccountId is not null)
            throw new ConflictException("The original provider contribution requires reconciliation.");
        else if (action == CustomerTableCheckoutAction.Start && attempt.State == AccountPaymentState.Reserved)
        {
            journal = await freezer.FreezeAsync(original.Owner, proof, original.Binding, participantToken, attempt, cancellationToken);
            context.AccountCheckoutJournals.Add(journal);
            AccountCheckoutFreeze.MarkStarting(attempt, journal, journal.StartedAt, attempt.CreatedBy);
            visit.RecordAccountChange();
            return true;
        }
        else if (action == CustomerTableCheckoutAction.Stop && attempt.State == AccountPaymentState.Reserved)
        {
            // The preceding provider-evidence refusal proves no frozen request escaped.
            attempt.State = AccountPaymentState.Released;
            attempt.CompletedAt = clock.GetUtcNow().UtcDateTime;
            attempt.Version++;
            visit.RecordAccountChange();
        }
        return false;
    }

    private void RequestStop(AccountPaymentAttempt attempt, AccountCheckoutJournal journal)
    {
        if (journal.CancelRequestedAt is not null || attempt.State is AccountPaymentState.Captured or AccountPaymentState.Released) return;
        journal.CancelRequestedAt = clock.GetUtcNow().UtcDateTime;
        journal.NextReconcileAt = journal.CancelRequestedAt.Value;
        if (attempt.State is AccountPaymentState.Starting or AccountPaymentState.Processing)
        { attempt.State = AccountPaymentState.CancelRequested; attempt.Version++; }
    }

    internal static void RequireJournal(AccountCheckoutJournal journal, CustomerTablePaymentProofDto proof)
    {
        if (journal.NativeOperationId != proof.OperationId || journal.NativeTenantId != proof.TenantId)
            throw Unavailable();
        AccountCheckoutReplayPayload.Frozen(journal);
    }
    private static NotFoundException Unavailable() => new("The original native contribution is unavailable.");
}
