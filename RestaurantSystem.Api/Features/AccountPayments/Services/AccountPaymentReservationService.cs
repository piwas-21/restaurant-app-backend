using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentReservationService(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITableGuestParticipantPaymentAuthorization guestAuthorization,
    IGuestAccountPaymentPolicy guestPolicy,
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
        ValidateReservationRequest(sessionId, operationId, request);
        return await ReserveCoreAsync(sessionId, operationId, null, request, guest: false, cancellationToken);
    }

    public async Task<AccountPaymentOperationDto> ReserveGuestAsync(
        Guid sessionId,
        Guid operationId,
        string? participantCredential,
        ReserveAccountPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRouteAndVersion(sessionId, operationId, request.ExpectedVersion);
        if (request.ExpectedAccountRevision <= 0)
            throw new BadRequestException("A positive account revision is required.");
        return await ReserveCoreAsync(
            sessionId, operationId, participantCredential, request, guest: true, cancellationToken);
    }

    private async Task<AccountPaymentOperationDto> ReserveCoreAsync(
        Guid sessionId,
        Guid operationId,
        string? participantCredential,
        ReserveAccountPaymentRequest request,
        bool guest,
        CancellationToken cancellationToken)
    {
        var staffActor = guest ? null : actors.ResolveStaffActor();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var actor = guest
                ? await guestAuthorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken)
                : staffActor!;
            var attempt = await LoadLockedAttemptAsync(sessionId, operationId, cancellationToken);
            RequireOwner(attempt, sessionId, actor);
            if (guest && attempt.PaymentMethod != PaymentMethod.OnlinePayment)
                throw new NotFoundException("The payment operation was not found for this table visit.");
            if (IsReservationReplay(attempt, request))
            {
                await transaction.CommitAsync(cancellationToken);
                return AccountPaymentSnapshots.ToOperation(attempt);
            }
            if (guest)
                guestPolicy.RequireContribution(attempt.AmountMinor, attempt.Currency);
            ValidateQuotedReservation(session, attempt, request);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            RequireUnexpiredQuote(attempt.QuoteExpiresAt, now);
            await ValidateEqualShareSlotAsync(attempt, sessionId, cancellationToken);

            var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
            var segments = ReadSegments(attempt);
            if (attempt.Currency != account.Money.Currency || AccountDebtMath.Total(segments) != attempt.AmountMinor)
                throw new ConflictException("The payment quote requires reconciliation before reservation.");
            AccountDebtMath.Subtract(account.Debt.Available, segments);

            MarkReserved(attempt, now, actor.AuditIdentifier);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountPaymentSnapshots.ToOperation(attempt);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The payment quote changed while it was being reserved.", exception);
        }
    }

    private void ValidateReservationRequest(
        Guid sessionId, Guid operationId, ReserveAccountPaymentRequest request)
    {
        if (!features.TableAccountPaymentsV1)
            throw new NotFoundException("Table account payments are not enabled.");
        ValidateRouteAndVersion(sessionId, operationId, request.ExpectedVersion);
        if (request.ExpectedAccountRevision <= 0)
            throw new BadRequestException("A positive account revision is required.");
    }

    private static bool IsReservationReplay(
        AccountPaymentAttempt attempt, ReserveAccountPaymentRequest request) =>
        attempt.State == AccountPaymentState.Reserved
        && attempt.Version == (long)request.ExpectedVersion + 1L
        && attempt.ExpectedAccountRevision == request.ExpectedAccountRevision;

    private static void ValidateQuotedReservation(
        TableServiceSession session, AccountPaymentAttempt attempt, ReserveAccountPaymentRequest request)
    {
        if (session.Status != TableServiceSessionStatus.Open)
            throw new ConflictException("A closed table visit cannot accept a payment reservation.");
        if (attempt.State != AccountPaymentState.Quoted || attempt.Version != request.ExpectedVersion)
            throw new ConflictException("The payment quote changed. Refresh it before reserving.");
        if (attempt.ExpectedAccountRevision != request.ExpectedAccountRevision
            || session.AccountRevision != attempt.ExpectedAccountRevision)
            throw new ConflictException("The account changed. Refresh the account before reserving.");
    }

    private static void RequireUnexpiredQuote(DateTime expiresAt, DateTime now)
    {
        if (expiresAt <= now)
            throw new ConflictException("The payment quote expired. Review a new quote before reserving.");
    }

    private async Task ValidateEqualShareSlotAsync(
        AccountPaymentAttempt attempt, Guid sessionId, CancellationToken cancellationToken)
    {
        if (attempt.EqualSharePlanId is not Guid planId)
            return;
        var plan = await context.AccountEqualSharePlans.SingleOrDefaultAsync(
            value => value.Id == planId && value.ServiceSessionId == sessionId, cancellationToken)
            ?? throw new ConflictException("The equal-share plan requires reconciliation.");
        if (plan.InvalidatedAt is not null)
            throw new ConflictException("The equal-share plan was superseded before reservation.");
        if (attempt.EqualShareOrdinal is not int ordinal || ordinal < 1 || ordinal > plan.ShareCount)
            throw new ConflictException("The equal-share position requires reconciliation.");
        if (await IsEqualShareSlotClaimedAsync(attempt.Id, planId, ordinal, cancellationToken))
            throw new ConflictException("This equal-share position already has a reserved or captured contribution.");
    }

    private Task<bool> IsEqualShareSlotClaimedAsync(
        Guid attemptId, Guid planId, int ordinal, CancellationToken cancellationToken) =>
        context.AccountPaymentAttempts.AnyAsync(value =>
            value.Id != attemptId && value.EqualSharePlanId == planId
            && value.EqualShareOrdinal == ordinal && ClaimedEqualShareStates.Contains(value.State),
            cancellationToken);

    private void MarkReserved(AccountPaymentAttempt attempt, DateTime now, string audit)
    {
        attempt.State = AccountPaymentState.Reserved;
        attempt.ReservedAt = now;
        attempt.ReservationExpiresAt = now.AddMinutes(options.Value.ReservationLifetimeMinutes);
        attempt.Version++;
        attempt.UpdatedAt = now;
        attempt.UpdatedBy = audit;
    }

    public async Task<AccountPaymentOperationDto> ReleaseAsync(
        Guid sessionId, Guid operationId, ReleaseAccountPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRouteAndVersion(sessionId, operationId, request.ExpectedVersion);
        return await ReleaseCoreAsync(sessionId, operationId, null, request, guest: false, cancellationToken);
    }

    public async Task<AccountPaymentOperationDto> ReleaseGuestAsync(
        Guid sessionId,
        Guid operationId,
        string? participantCredential,
        ReleaseAccountPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRouteAndVersion(sessionId, operationId, request.ExpectedVersion);
        return await ReleaseCoreAsync(
            sessionId, operationId, participantCredential, request, guest: true, cancellationToken);
    }

    private async Task<AccountPaymentOperationDto> ReleaseCoreAsync(
        Guid sessionId,
        Guid operationId,
        string? participantCredential,
        ReleaseAccountPaymentRequest request,
        bool guest,
        CancellationToken cancellationToken)
    {
        var staffActor = guest ? null : actors.ResolveStaffActor();
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var actor = guest
                ? await guestAuthorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken)
                : staffActor!;
            var attempt = await LoadLockedAttemptAsync(sessionId, operationId, cancellationToken);
            RequireOwner(attempt, sessionId, actor);
            if (guest && attempt.PaymentMethod != PaymentMethod.OnlinePayment)
                throw new NotFoundException("The payment operation was not found for this table visit.");
            if (attempt.State == AccountPaymentState.Released
                && attempt.Version == (long)request.ExpectedVersion + 1L)
            {
                await transaction.CommitAsync(cancellationToken);
                return AccountPaymentSnapshots.ToOperation(attempt);
            }
            if (attempt.Version != request.ExpectedVersion
                || !attempt.State.CanReleaseLocally()
                || attempt.StartedAt is not null
                || attempt.ProviderSessionId is not null
                || attempt.ProviderChargeId is not null)
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
