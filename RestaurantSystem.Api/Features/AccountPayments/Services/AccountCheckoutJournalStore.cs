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
        // Native reservations require their original proof and separately approved native return rail.
        if (await context.Set<CustomerTablePaymentOperation>().AnyAsync(row => row.AttemptId == attempt.Id, cancellationToken))
            throw Unavailable();
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
        AccountCheckoutFreeze.RequireReserved(attempt, expectedVersion, now);
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
        var scope = attempt.Allocations.Select(value => new AccountDebtSegment(
            value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit)).ToArray();
        if (scope.Length == 0 || attempt.Currency != account.Money.Currency
            || AccountDebtMath.Total(scope) != attempt.AmountMinor)
            throw new ConflictException("The reserved contribution requires reconciliation.");
        AccountDebtMath.Subtract(account.Debt.Outstanding, scope);
        journal = AccountCheckoutFreeze.Create(attempt, receiptHash, now, options.Value, provider);
        context.AccountCheckoutJournals.Add(journal);
        AccountCheckoutFreeze.MarkStarting(attempt, journal, now, actor.AuditIdentifier);
        session.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return journal;
    }

    public async Task<AccountCheckoutJournal> ReadAsync(Guid attemptId, CancellationToken cancellationToken) =>
        await context.AccountCheckoutJournals.AsNoTracking().SingleOrDefaultAsync(
            value => value.AttemptId == attemptId, cancellationToken) ?? throw Unavailable();

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

    private static NotFoundException Unavailable() => new("The payment operation is unavailable for this visit.");
}
