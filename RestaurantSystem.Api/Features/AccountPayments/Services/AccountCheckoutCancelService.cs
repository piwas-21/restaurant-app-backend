using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Cancellation requests retain the reservation until canonical non-payment is verified.</summary>
public sealed class AccountCheckoutCancelService(ApplicationDbContext context,
    ITableGuestParticipantPaymentAuthorization authorization, IAccountCheckoutReconciler reconciler,
    TimeProvider clock) : IAccountCheckoutCancelService
{
    public async Task<AccountCheckoutStartDto> RequestGuestAsync(Guid sessionId, Guid operationId,
        int expectedVersion, string? participantCredential, CancellationToken cancellationToken)
    {
        var attemptId = await RecordRequestAsync(sessionId, operationId, expectedVersion,
            participantCredential, cancellationToken);
        return await reconciler.ReconcileAsync(attemptId, cancellationToken);
    }

    private async Task<Guid> RecordRequestAsync(Guid sessionId, Guid operationId, int expectedVersion,
        string? participantCredential, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || operationId == Guid.Empty || expectedVersion <= 0)
            throw new BadRequestException("The original visit, operation and positive payment version are required.");
        if (context.Database.CurrentTransaction is not null)
            throw new ConflictException("Cancellation must own its short transaction.");
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
            ?? throw Unavailable();
        var actor = await authorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken);
        var attemptId = await context.AccountPaymentAttempts.AsNoTracking().Where(value =>
            value.ServiceSessionId == sessionId && value.OperationId == operationId
            && value.ActorId == actor.ActorId && value.ActorKind == actor.Kind)
            .Select(value => value.Id).SingleOrDefaultAsync(cancellationToken);
        if (attemptId == Guid.Empty) throw Unavailable();
        var locked = await AccountCheckoutLocks.LoadAsync(context, attemptId, cancellationToken);
        var attempt = locked.Attempt;
        if (locked.Journal.CancelRequestedAt is not null
            || attempt.State is AccountPaymentState.Captured or AccountPaymentState.Released)
        {
            await transaction.CommitAsync(cancellationToken);
            return attemptId;
        }
        if (attempt.Version != expectedVersion
            || attempt.State is not (AccountPaymentState.Starting or AccountPaymentState.Processing))
            throw new ConflictException("Review the original checkout status before requesting cancellation.");
        var now = clock.GetUtcNow().UtcDateTime;
        locked.Journal.CancelRequestedAt = now;
        locked.Journal.NextReconcileAt = now;
        attempt.State = AccountPaymentState.CancelRequested;
        attempt.Version++;
        attempt.UpdatedAt = now;
        attempt.UpdatedBy = actor.AuditIdentifier;
        locked.Session.RecordAccountChange();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return attemptId;
    }

    private static NotFoundException Unavailable() => new("The original guest contribution is unavailable.");
}
