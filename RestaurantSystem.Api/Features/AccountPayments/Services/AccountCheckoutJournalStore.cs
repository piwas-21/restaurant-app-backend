using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>The original provider request and private receipt grant commit before any network call.</summary>
public sealed class AccountCheckoutJournalStore(ApplicationDbContext context,
    ITableGuestParticipantPaymentAuthorization authorization, IGuestAccountPaymentPolicy policy,
    IAccountStripeCheckoutClient provider, IOptions<AccountCheckoutSettings> options, TimeProvider clock)
    : IAccountCheckoutJournalStore
{
    public async Task<AccountCheckoutJournal> FreezeGuestAsync(Guid sessionId, Guid operationId,
        int expectedVersion, string? participantCredential, string? receiptCredential,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || operationId == Guid.Empty || expectedVersion <= 0
            || !AccountReceiptCredentialCrypto.TryHash(receiptCredential, out var receiptHash))
            throw new BadRequestException("A visit, original payment version and private receipt credential are required.");
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Checkout must own its short money transaction.");
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
            ?? throw Unavailable();
        var actor = await authorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken);
        var attempt = await context.AccountPaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM account_payment_attempts WHERE operation_id = {operationId} FOR UPDATE")
            .Include(value => value.Allocations).SingleOrDefaultAsync(cancellationToken) ?? throw Unavailable();
        RequireOwner(attempt, sessionId, actor);
        var journal = await context.AccountCheckoutJournals.SingleOrDefaultAsync(
            value => value.AttemptId == attempt.Id, cancellationToken);
        if (journal is not null)
        {
            RequireOriginalStart(journal, expectedVersion, receiptCredential);
            await transaction.CommitAsync(cancellationToken);
            return journal;
        }
        policy.RequireContribution(attempt.AmountMinor, attempt.Currency);
        // Provider dates have whole-second precision; the persisted replay payload must match.
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()).UtcDateTime;
        RequireReserved(attempt, expectedVersion, now);
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
        var scope = attempt.Allocations.Select(value => new AccountDebtSegment(
            value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit)).ToArray();
        if (scope.Length == 0 || attempt.Currency != account.Money.Currency
            || AccountDebtMath.Total(scope) != attempt.AmountMinor)
            throw new ConflictException("The reserved contribution requires reconciliation.");
        AccountDebtMath.Subtract(account.Debt.Outstanding, scope);
        journal = CreateJournal(attempt, receiptHash, now);
        context.AccountCheckoutJournals.Add(journal);
        attempt.State = AccountPaymentState.Starting;
        attempt.StartedAt = now;
        attempt.ProviderAccountId = journal.ProviderAccountId;
        attempt.Version++;
        attempt.UpdatedAt = now;
        attempt.UpdatedBy = actor.AuditIdentifier;
        session.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return journal;
    }

    public async Task<AccountCheckoutJournal> ReadAsync(Guid attemptId, CancellationToken cancellationToken) =>
        await context.AccountCheckoutJournals.AsNoTracking().SingleOrDefaultAsync(
            value => value.AttemptId == attemptId, cancellationToken) ?? throw Unavailable();

    private AccountCheckoutJournal CreateJournal(AccountPaymentAttempt attempt, string receiptHash, DateTime now)
    {
        var request = new AccountStripeCheckoutRequest
        {
            AttemptId = attempt.Id,
            AmountMinor = attempt.AmountMinor,
            Currency = attempt.Currency,
            Context = provider.ReadContext(),
            ReturnBaseUrl = provider.ReadReturnBaseUrl(),
            IdempotencyKey = AccountCheckoutReplayPayload.CreateKey(attempt.Id),
            ExpiresAt = now.AddMinutes(options.Value.CheckoutLifetimeMinutes)
        };
        return new AccountCheckoutJournal
        {
            Id = Guid.NewGuid(),
            AttemptId = attempt.Id,
            StartedAttemptVersion = attempt.Version,
            AmountMinor = request.AmountMinor,
            Currency = request.Currency,
            ProviderAccountId = request.Context.ConnectedAccountId,
            ProviderLiveMode = request.Context.LiveMode,
            ReturnBaseUrl = request.ReturnBaseUrl,
            CreateIdempotencyKey = request.IdempotencyKey,
            CreatePayloadHash = AccountCheckoutReplayPayload.Hash(request),
            StartedAt = now,
            ExpiresAt = request.ExpiresAt,
            MaximumCreateRetryAt = now.AddHours(options.Value.MaximumCreateRetryHours),
            NextReconcileAt = now,
            ReceiptCredentialHash = receiptHash,
            ReceiptExpiresAt = now.AddHours(options.Value.ReceiptLifetimeHours),
            CreatedBy = attempt.CreatedBy
        };
    }

    private static void RequireOwner(AccountPaymentAttempt attempt, Guid sessionId, AccountPaymentActor actor)
    {
        if (attempt.ServiceSessionId != sessionId || attempt.ActorId != actor.ActorId
            || attempt.ActorKind != AccountPaymentActorKind.GuestParticipant
            || actor.Kind != AccountPaymentActorKind.GuestParticipant)
            throw Unavailable();
    }

    private static void RequireOriginalStart(AccountCheckoutJournal journal, int version, string? receiptCredential)
    {
        if (journal.StartedAttemptVersion != version
            || !AccountReceiptCredentialCrypto.Verify(receiptCredential, journal.ReceiptCredentialHash))
            throw new ConflictException("Recover the original checkout with its original receipt credential and version.");
    }

    private static void RequireReserved(AccountPaymentAttempt attempt, int version, DateTime now)
    {
        if (attempt.PaymentMethod != PaymentMethod.OnlinePayment || attempt.State != AccountPaymentState.Reserved
            || attempt.Version != version || attempt.AmountMinor <= 0
            || attempt.ReservationExpiresAt is not DateTime expires || expires <= now)
            throw new ConflictException("Review and reserve this online contribution before starting checkout.");
    }

    private static NotFoundException Unavailable() => new("The payment operation is unavailable for this visit.");
}
