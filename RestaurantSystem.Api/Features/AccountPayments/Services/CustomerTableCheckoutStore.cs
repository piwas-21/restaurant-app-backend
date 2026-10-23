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
    ITableGuestParticipantPaymentAuthorization participants, IGuestAccountPaymentPolicy policy,
    IAccountStripeCheckoutClient provider, IOptions<AccountCheckoutSettings> checkout,
    IOptions<CustomerHostedCheckoutSettings> hosted, TimeProvider clock) : ICustomerTableCheckoutStore
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
            var hasJournal = false;
            if (decision.AttemptId is Guid attemptId)
            {
                if (visit is null) throw Unavailable();
                var attempt = await context.AccountPaymentAttempts
                    .FromSqlInterpolated($"SELECT * FROM account_payment_attempts WHERE id = {attemptId} FOR UPDATE")
                    .Include(row => row.Allocations).SingleAsync(cancellationToken);
                var journal = await context.AccountCheckoutJournals
                    .FromSqlInterpolated($"SELECT * FROM account_checkout_journals WHERE attempt_id = {attemptId} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken);
                if (journal is not null)
                {
                    RequireJournal(journal, proof);
                    if (action == CustomerTableCheckoutAction.Stop) RequestStop(attempt, journal);
                    hasJournal = true;
                }
                else if (attempt.StartedAt is not null || attempt.ProviderSessionId is not null
                    || attempt.ProviderChargeId is not null || attempt.ProviderAccountId is not null)
                    throw new ConflictException("The original provider contribution requires reconciliation.");
                else if (action == CustomerTableCheckoutAction.Start && attempt.State == AccountPaymentState.Reserved)
                {
                    journal = await FreezeAsync(owner, proof, binding, participantToken, attempt, cancellationToken);
                    context.AccountCheckoutJournals.Add(journal);
                    AccountCheckoutFreeze.MarkStarting(attempt, journal, journal.StartedAt, attempt.CreatedBy);
                    hasJournal = true;
                    visit.RecordAccountChange();
                }
                else if (action == CustomerTableCheckoutAction.Stop && attempt.State == AccountPaymentState.Reserved)
                {
                    // Without a frozen request, no provider call is authorized to have escaped.
                    if (attempt.StartedAt is not null || attempt.ProviderSessionId is not null
                        || attempt.ProviderChargeId is not null || attempt.ProviderAccountId is not null)
                        throw new ConflictException("The original contribution requires provider reconciliation.");
                    attempt.State = AccountPaymentState.Released;
                    attempt.CompletedAt = clock.GetUtcNow().UtcDateTime;
                    attempt.Version++;
                    visit.RecordAccountChange();
                }
            }
            if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != owner) throw Unavailable();
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(owner, binding, decision.AttemptId, hasJournal);
        }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    private async Task<AccountCheckoutJournal> FreezeAsync(CustomerTablePaymentOwner owner,
        CustomerTablePaymentProofDto proof, CustomerTablePaymentBinding binding, string? participantToken,
        AccountPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var currency = await access.RequireNewAsync(owner, proof.TenantId, cancellationToken);
        if (!TableGuestCredentialCrypto.TryHashParticipantToken(participantToken, out var tokenHash)
            || tokenHash != binding.ParticipantTokenHash) throw Unavailable();
        var actor = await participants.AuthorizeActiveAsync(binding.ServiceSessionId, participantToken, cancellationToken);
        if (actor.ActorId != binding.ParticipantId || currency != binding.Currency) throw Unavailable();
        if (!await context.TableServiceSessions.AnyAsync(row => row.Id == binding.ServiceSessionId
            && row.TableId == binding.TableId, cancellationToken)) throw Unavailable();
        policy.RequireContribution(attempt.AmountMinor, attempt.Currency);
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()).UtcDateTime;
        AccountCheckoutFreeze.RequireReserved(attempt, attempt.Version, now);
        var debt = await new AccountDebtSnapshotReader(context).ReadAsync(binding.ServiceSessionId, cancellationToken);
        var scope = attempt.Allocations.Select(row => new AccountDebtSegment(row.OrderId, row.OrderItemId,
            row.StartOrdinal, row.UnitCount, row.MinorPerUnit)).ToArray();
        if (scope.Length == 0 || debt.Money.Currency != binding.Currency
            || AccountDebtMath.Total(scope) != binding.AmountMinor)
            throw new ConflictException("The original contribution requires reconciliation.");
        AccountDebtMath.Subtract(debt.Debt.Outstanding, scope);
        var native = new NativeAccountCheckoutScope(proof.TenantId, proof.OperationId);
        var result = AccountCheckoutFreeze.Create(attempt, null, now, checkout.Value, provider, native,
            native.ReturnBase(hosted.Value.LinkOrigin, hosted.Value.TenantSlug, attempt.Id));
        // Revalidate authority after all asynchronous quote/debt reads while the visit remains locked.
        if (await access.RequireNewAsync(owner, proof.TenantId, cancellationToken) != binding.Currency
            || (await participants.AuthorizeActiveAsync(binding.ServiceSessionId, participantToken, cancellationToken)).ActorId != binding.ParticipantId)
            throw Unavailable();
        return result;
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
