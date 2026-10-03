using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentReservationService(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITenantFeatures features,
    IOptions<AccountPaymentSettings> options,
    TimeProvider timeProvider) : IAccountPaymentReservationService
{
    private static readonly AccountPaymentState[] ClaimedEqualShareStates =
    [
        AccountPaymentState.Reserved,
        AccountPaymentState.Starting,
        AccountPaymentState.Processing,
        AccountPaymentState.Captured,
        AccountPaymentState.CancelRequested,
        AccountPaymentState.ReconciliationRequired
    ];

    public async Task<AccountPaymentOperationDto> ReserveAsync(
        Guid sessionId, Guid operationId, ReserveAccountPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!features.TableAccountPaymentsV1)
            throw new NotFoundException("Table account payments are not enabled.");
        ValidateRouteAndVersion(sessionId, operationId, request.ExpectedVersion);
        if (request.ExpectedAccountRevision <= 0)
            throw new BadRequestException("A positive account revision is required.");
        var actor = actors.ResolveStaffActor();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var attempt = await LoadLockedAttemptAsync(sessionId, operationId, cancellationToken);
            RequireOwner(attempt, sessionId, actor);
            if (attempt.State == AccountPaymentState.Reserved
                && attempt.Version == (long)request.ExpectedVersion + 1L
                && attempt.ExpectedAccountRevision == request.ExpectedAccountRevision)
            {
                await transaction.CommitAsync(cancellationToken);
                return AccountPaymentSnapshots.ToOperation(attempt);
            }
            if (session.Status != TableServiceSessionStatus.Open)
                throw new ConflictException("A closed table visit cannot accept a payment reservation.");
            if (attempt.State != AccountPaymentState.Quoted || attempt.Version != request.ExpectedVersion)
                throw new ConflictException("The payment quote changed. Refresh it before reserving.");
            if (attempt.ExpectedAccountRevision != request.ExpectedAccountRevision
                || session.AccountRevision != attempt.ExpectedAccountRevision)
                throw new ConflictException("The account changed. Refresh the account before reserving.");

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (attempt.QuoteExpiresAt <= now)
                throw new ConflictException("The payment quote expired. Review a new quote before reserving.");
            if (attempt.EqualSharePlanId is Guid planId)
            {
                var plan = await context.AccountEqualSharePlans.SingleOrDefaultAsync(
                    value => value.Id == planId && value.ServiceSessionId == sessionId, cancellationToken)
                    ?? throw new ConflictException("The equal-share plan requires reconciliation.");
                if (plan.InvalidatedAt is not null)
                    throw new ConflictException("The equal-share plan was superseded before reservation.");
                if (attempt.EqualShareOrdinal is not int ordinal || ordinal < 1 || ordinal > plan.ShareCount)
                    throw new ConflictException("The equal-share position requires reconciliation.");
                var claimed = await context.AccountPaymentAttempts.AnyAsync(value =>
                    value.Id != attempt.Id && value.EqualSharePlanId == planId
                    && value.EqualShareOrdinal == ordinal && ClaimedEqualShareStates.Contains(value.State),
                    cancellationToken);
                if (claimed)
                {
                    throw new ConflictException(
                        "This equal-share position already has a reserved or captured contribution.");
                }
            }

            var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
            var segments = ReadSegments(attempt);
            if (attempt.Currency != account.Money.Currency || AccountDebtMath.Total(segments) != attempt.AmountMinor)
                throw new ConflictException("The payment quote requires reconciliation before reservation.");
            AccountDebtMath.Subtract(account.Debt.Available, segments);

            attempt.State = AccountPaymentState.Reserved;
            attempt.ReservedAt = now;
            attempt.ReservationExpiresAt = now.AddMinutes(options.Value.ReservationLifetimeMinutes);
            attempt.Version++;
            attempt.UpdatedAt = now;
            attempt.UpdatedBy = actor.AuditIdentifier;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountPaymentSnapshots.ToOperation(attempt);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The payment quote changed while it was being reserved.", exception);
        }
    }

    public async Task<AccountPaymentOperationDto> ReleaseAsync(
        Guid sessionId, Guid operationId, ReleaseAccountPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRouteAndVersion(sessionId, operationId, request.ExpectedVersion);
        var actor = actors.ResolveStaffActor();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            _ = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var attempt = await LoadLockedAttemptAsync(sessionId, operationId, cancellationToken);
            RequireOwner(attempt, sessionId, actor);
            if (attempt.State == AccountPaymentState.Released
                && attempt.Version == (long)request.ExpectedVersion + 1L)
            {
                await transaction.CommitAsync(cancellationToken);
                return AccountPaymentSnapshots.ToOperation(attempt);
            }
            if (attempt.Version != request.ExpectedVersion || !attempt.State.CanReleaseLocally())
                throw new ConflictException("This payment attempt may have reached a provider and cannot be released locally.");

            attempt.State = AccountPaymentState.Released;
            attempt.Version++;
            attempt.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            attempt.UpdatedBy = actor.AuditIdentifier;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountPaymentSnapshots.ToOperation(attempt);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The payment attempt changed while it was being released.", exception);
        }
    }

    private async Task<AccountPaymentAttempt> LoadLockedAttemptAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken) =>
        await context.AccountPaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM account_payment_attempts WHERE service_session_id = {sessionId} AND operation_id = {operationId} FOR UPDATE")
            .Include(value => value.Allocations)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("The payment operation was not found for this table visit.");

    private static AccountDebtSegment[] ReadSegments(AccountPaymentAttempt attempt)
    {
        if (attempt.Allocations.Count == 0 || attempt.Allocations.Any(value =>
                value.OrderId == Guid.Empty || value.UnitCount <= 0 || value.StartOrdinal < 1
                || value.MinorPerUnit <= 0 || value.AmountMinor != checked(value.MinorPerUnit * value.UnitCount)))
            throw new ConflictException("The payment quote contains an invalid frozen allocation.");
        var segments = attempt.Allocations.Select(value => new AccountDebtSegment(
            value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit)).ToArray();
        if (segments.GroupBy(value => (value.OrderId, value.OrderItemId)).Any(group =>
                group.OrderBy(value => value.StartOrdinal).Zip(group.OrderBy(value => value.StartOrdinal).Skip(1),
                    (first, next) => first.EndExclusive > next.StartOrdinal).Any(value => value)))
            throw new ConflictException("The payment quote contains overlapping allocation ranges.");
        return segments;
    }

    private static void RequireOwner(AccountPaymentAttempt attempt, Guid sessionId, AccountPaymentActor actor)
    {
        if (attempt.ServiceSessionId != sessionId || attempt.ActorId != actor.ActorId
            || attempt.ActorKind != actor.Kind)
            throw new NotFoundException("The payment operation was not found for this table visit.");
    }

    private static void ValidateRouteAndVersion(Guid sessionId, Guid operationId, int expectedVersion)
    {
        if (sessionId == Guid.Empty || operationId == Guid.Empty)
            throw new BadRequestException("A table visit and payment operation are required.");
        if (expectedVersion <= 0)
            throw new BadRequestException("A positive payment version is required.");
    }
}
