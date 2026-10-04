using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCashCaptureReceiptPolicy
{
    internal static AccountCashCollectionReceipt? Create(
        AccountPaymentAttempt attempt, AccountPaymentQuoteSnapshot snapshot,
        AccountPaymentActor actor, CaptureAccountPaymentRequest request,
        string auditIdentifier, DateTime capturedAt)
    {
        RequireCashCaptureContext(attempt, snapshot, actor, request, auditIdentifier);
        if (attempt.PaymentMethod != PaymentMethod.Cash)
        {
            if (request.ReceivedMinor is not null)
                throw new BadRequestException("Received cash is only valid for a cash contribution.");
            return null;
        }

        var quote = snapshot.CashSettlement
            ?? throw new ConflictException("This legacy cash quote has no frozen settlement terms.");
        AccountCashSettlementPolicy.RequireMatches(quote, attempt.Currency,
            attempt.PaymentMethod, attempt.AmountMinor);
        var received = request.ReceivedMinor
            ?? throw new BadRequestException("Enter the cash received before completing collection.");
        if (received < quote.DueAmountMinor)
            throw new BadRequestException("The cash received is below the amount due.");
        var actorRole = actor.Role
            ?? throw new ConflictException("The cash collection role requires reconciliation.");

        return new AccountCashCollectionReceipt
        {
            Id = Guid.NewGuid(),
            AttemptId = attempt.Id,
            PolicyVersion = quote.PolicyVersion,
            Currency = quote.Currency,
            PaymentMethod = quote.PaymentMethod,
            ExactAmountMinor = quote.ExactAmountMinor,
            AdjustmentMinor = quote.AdjustmentMinor,
            DueAmountMinor = quote.DueAmountMinor,
            ReceivedMinor = received,
            ChangeMinor = checked(received - quote.DueAmountMinor),
            ExpectedAccountRevision = snapshot.ExpectedAccountRevision,
            ExpectedVersion = request.ExpectedVersion,
            RequestHash = HashRequest(attempt, actor, quote,
                snapshot.ExpectedAccountRevision, request.ExpectedVersion, received),
            ActorId = actor.ActorId,
            ActorKind = actor.Kind,
            ActorRole = actorRole,
            CapturedAt = capturedAt,
            CreatedAt = capturedAt,
            CreatedBy = auditIdentifier
        };
    }

    internal static void RequireReplay(
        AccountPaymentAttempt attempt, AccountPaymentQuoteSnapshot snapshot,
        AccountPaymentActor actor, CaptureAccountPaymentRequest request)
    {
        if (attempt.ActorId != actor.ActorId || attempt.ActorKind != actor.Kind)
            throw new ConflictException("The saved payment collection request does not match this replay.");
        if (attempt.PaymentMethod != PaymentMethod.Cash)
        {
            if (request.ReceivedMinor is not null || attempt.CashCollectionReceipt is not null)
                throw new ConflictException("The saved payment collection request does not match this replay.");
            return;
        }

        ValidateStored(attempt, snapshot);
        var receipt = attempt.CashCollectionReceipt;
        if (receipt is null)
        {
            // Old captured cash remains readable as exact historical money, without a fabricated receipt.
            if (snapshot.CashSettlement is null && request.ReceivedMinor is null
                && attempt.State == AccountPaymentState.Captured
                && attempt.Version > 1 && request.ExpectedVersion == attempt.Version - 1)
                return;
            throw new ConflictException("The cash collection receipt is unavailable for replay.");
        }

        var quote = snapshot.CashSettlement
            ?? throw new ConflictException("The saved cash quote has no frozen settlement terms.");
        AccountCashSettlementPolicy.RequireMatches(quote, attempt.Currency,
            attempt.PaymentMethod, attempt.AmountMinor);
        var received = request.ReceivedMinor;
        var originalActor = actor with { Role = receipt.ActorRole };
        if (received is null
            || receipt.AttemptId != attempt.Id
            || receipt.ActorId != actor.ActorId
            || receipt.ActorKind != actor.Kind
            || receipt.ExpectedVersion != request.ExpectedVersion
            || receipt.ExpectedAccountRevision != snapshot.ExpectedAccountRevision
            || attempt.Version != receipt.ExpectedVersion + 1
            || receipt.PolicyVersion != quote.PolicyVersion
            || receipt.Currency != quote.Currency
            || receipt.PaymentMethod != quote.PaymentMethod
            || receipt.ExactAmountMinor != quote.ExactAmountMinor
            || receipt.AdjustmentMinor != quote.AdjustmentMinor
            || receipt.DueAmountMinor != quote.DueAmountMinor
            || receipt.ReceivedMinor != received
            || receipt.ChangeMinor != checked(received.Value - quote.DueAmountMinor)
            || receipt.RequestHash != HashRequest(attempt, originalActor, quote,
                snapshot.ExpectedAccountRevision, request.ExpectedVersion, received.Value))
            throw new ConflictException("The saved cash collection request does not match this replay.");
    }

    internal static void ValidateStored(AccountPaymentAttempt attempt, AccountPaymentQuoteSnapshot snapshot)
    {
        var receipt = attempt.CashCollectionReceipt;
        if (attempt.PaymentMethod == PaymentMethod.Cash
            && (snapshot.PaymentMethod != attempt.PaymentMethod
                || snapshot.ExpectedAccountRevision != attempt.ExpectedAccountRevision
                || snapshot.AmountMinor != attempt.AmountMinor
                || snapshot.Currency != attempt.Currency))
            throw new ConflictException("The cash quote snapshot requires reconciliation.");
        if (receipt is null)
        {
            if (attempt.State == AccountPaymentState.Captured && snapshot.CashSettlement is not null
                && attempt.PaymentMethod == PaymentMethod.Cash)
                throw new ConflictException("The cash collection receipt requires reconciliation.");
            return;
        }

        var quote = snapshot.CashSettlement;
        if (quote is null || attempt.PaymentMethod != PaymentMethod.Cash
            || attempt.State != AccountPaymentState.Captured
            || receipt.AttemptId != attempt.Id || receipt.ActorId != attempt.ActorId
            || receipt.ActorKind != attempt.ActorKind
            || receipt.ExpectedAccountRevision != snapshot.ExpectedAccountRevision
            || receipt.ExpectedVersion <= 0 || receipt.ExpectedVersion == int.MaxValue
            || attempt.Version != receipt.ExpectedVersion + 1
            || receipt.ActorId == Guid.Empty || receipt.ActorKind != AccountPaymentActorKind.Staff
            || !IsAuthorizedCashRole(receipt.ActorRole)
            || receipt.CapturedAt == default
            || attempt.CompletedAt != receipt.CapturedAt || receipt.CreatedAt != receipt.CapturedAt
            || !Enum.IsDefined(receipt.ActorKind)
            || receipt.ReceivedMinor < receipt.DueAmountMinor
            || receipt.ChangeMinor != receipt.ReceivedMinor - receipt.DueAmountMinor
            || receipt.RequestHash.Length != 64)
            throw new ConflictException("The saved cash collection receipt requires reconciliation.");
        AccountCashSettlementPolicy.RequireMatches(quote, attempt.Currency,
            attempt.PaymentMethod, attempt.AmountMinor);
        if (receipt.PolicyVersion != quote.PolicyVersion || receipt.Currency != quote.Currency
            || receipt.PaymentMethod != quote.PaymentMethod || receipt.ExactAmountMinor != quote.ExactAmountMinor
            || receipt.AdjustmentMinor != quote.AdjustmentMinor || receipt.DueAmountMinor != quote.DueAmountMinor)
            throw new ConflictException("The saved cash collection receipt differs from its quote.");
        var actor = new AccountPaymentActor(
            receipt.ActorId, receipt.ActorKind, attempt.CreatedBy, receipt.ActorRole);
        if (receipt.RequestHash != HashRequest(attempt, actor, quote,
                receipt.ExpectedAccountRevision, receipt.ExpectedVersion, receipt.ReceivedMinor))
            throw new ConflictException("The saved cash collection request hash requires reconciliation.");
    }

    private static string HashRequest(
        AccountPaymentAttempt attempt, AccountPaymentActor actor, CashSettlementQuote quote,
        long expectedAccountRevision, int expectedVersion, long receivedMinor) =>
        AccountPaymentSnapshots.Hash(new
        {
            attempt.ServiceSessionId,
            attempt.OperationId,
            attempt.Id,
            actor.ActorId,
            actor.Kind,
            actor.Role,
            attempt.PayloadHash,
            expectedAccountRevision,
            expectedVersion,
            receivedMinor,
            cashSettlement = quote
        });

    private static void RequireCashCaptureContext(
        AccountPaymentAttempt attempt, AccountPaymentQuoteSnapshot snapshot,
        AccountPaymentActor actor, CaptureAccountPaymentRequest request, string auditIdentifier)
    {
        if (attempt.State != AccountPaymentState.Reserved || attempt.ActorId != actor.ActorId
            || attempt.ActorKind != actor.Kind || actor.Kind != AccountPaymentActorKind.Staff
            || !IsAuthorizedCashRole(actor.Role)
            || actor.ActorId == Guid.Empty || !Enum.IsDefined(actor.Kind)
            || string.IsNullOrWhiteSpace(auditIdentifier) || request.ExpectedVersion <= 0
            || request.ExpectedVersion == int.MaxValue || request.ExpectedVersion != attempt.Version
            || snapshot.ExpectedAccountRevision <= 0
            || snapshot.ExpectedAccountRevision != attempt.ExpectedAccountRevision
            || snapshot.PaymentMethod != attempt.PaymentMethod || snapshot.AmountMinor != attempt.AmountMinor
            || snapshot.Currency != attempt.Currency)
            throw new ConflictException("The reserved cash collection does not match its reviewed quote.");
    }

    private static bool IsAuthorizedCashRole(UserRole? role) =>
        role is UserRole.Admin or UserRole.Cashier or UserRole.Server;
}
