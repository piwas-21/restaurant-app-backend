using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentAccountReader(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITableGuestParticipantPaymentAuthorization guestAuthorization,
    IGuestAccountPaymentPolicy guestPolicy,
    ITenantFeatures features,
    IOptions<AccountPaymentSettings> options) : IAccountPaymentAccountReader
{
    private static readonly AccountPaymentState[] NonHoldingStates =
    [
        AccountPaymentState.Quoted,
        AccountPaymentState.Captured,
        AccountPaymentState.Released,
        AccountPaymentState.Failed
    ];

    public async Task<AccountPaymentAccountDto> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!features.TableAccountPaymentsV1)
            throw new NotFoundException("Table account payments are not enabled.");
        if (sessionId == Guid.Empty)
            throw new BadRequestException("A table visit is required.");

        var actor = actors.ResolveStaffActor();
        return await ReadAsync(sessionId, actor, null, cancellationToken);
    }

    public async Task<AccountPaymentAccountDto> GetGuestAsync(
        Guid sessionId, string? participantCredential, CancellationToken cancellationToken)
    {
        guestPolicy.RequireAccountRead();
        if (sessionId == Guid.Empty)
            throw new BadRequestException("A table visit is required.");
        return await ReadAsync(sessionId, null, participantCredential, cancellationToken);
    }

    private async Task<AccountPaymentAccountDto> ReadAsync(
        Guid sessionId, AccountPaymentActor? knownActor, string? participantCredential,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;

        var actor = knownActor ?? await guestAuthorization.AuthorizeActiveAsync(
            sessionId, participantCredential, cancellationToken);
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
        var settings = options.Value;
        if (account.Debt.Outstanding.Count > settings.MaximumScopeSegments
            || account.Debt.Available.Count > settings.MaximumScopeSegments)
            throw new ConflictException("The table account exceeds the configured allocation display limit.");

        var plan = await ReadActivePlanAsync(sessionId, account, actor, settings.MaximumScopeSegments,
            settings.MaximumEqualShares, cancellationToken);
        var attempts = await ReadActiveAttemptsAsync(sessionId, actor, settings.MaximumActiveAttemptSummaries,
            cancellationToken);
        var capturedMinor = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId && value.State == AccountPaymentState.Captured)
            .Select(value => (long?)value.AmountMinor)
            .SumAsync(cancellationToken) ?? 0L;

        var dto = new AccountPaymentAccountDto(
            sessionId,
            account.Session.Status,
            account.Session.AccountRevision,
            account.Money.Currency,
            account.Debt.OutstandingMinor,
            account.Debt.ReservedMinor,
            account.Debt.AvailableMinor,
            capturedMinor,
            AccountPaymentSnapshots.ToDtos(account.Debt.Outstanding),
            AccountPaymentSnapshots.ToDtos(account.Debt.Available),
            plan,
            attempts,
            new AccountPaymentLimitsDto(settings.MaximumSelectedUnits, settings.MaximumEqualShares,
                knownActor is null ? guestPolicy.ReadLimits(account.Money.Currency) : null));
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        if (knownActor is null)
        {
            var currentActor = await guestAuthorization.AuthorizeActiveAsync(
                sessionId, participantCredential, cancellationToken);
            if (currentActor.ActorId != actor.ActorId || currentActor.Kind != actor.Kind)
                throw new NotFoundException("Guest access is unavailable for this table visit.");
        }
        return dto;
    }

    private async Task<AccountPaymentEqualShareSummaryDto?> ReadActivePlanAsync(
        Guid sessionId, AccountPaymentAccountSnapshot account, AccountPaymentActor actor,
        int maximumSegments, int maximumShares, CancellationToken cancellationToken)
    {
        var plans = await context.AccountEqualSharePlans.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId && value.InvalidatedAt == null)
            .OrderBy(value => value.CreatedAt)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (plans.Count > 1)
            throw new ConflictException("The table account has multiple active equal-share plans to reconcile.");
        var plan = plans.SingleOrDefault();
        if (plan is null) return null;
        var scope = AccountPaymentSnapshots.ReadScope(plan.ScopeJson);
        if (scope.Count > maximumSegments || plan.ShareCount < 2 || plan.ShareCount > maximumShares
            || AccountDebtMath.Total(scope) != plan.TotalMinor || plan.TotalMinor < plan.ShareCount)
            throw new ConflictException("The equal-share plan exceeds the configured allocation display limit.");
        var claims = await ReadShareClaimsAsync(sessionId, plan.Id, plan.ShareCount, cancellationToken);
        var slots = Enumerable.Range(1, plan.ShareCount).Select(ordinal =>
        {
            var share = AccountEqualScopeMath.ForShare(scope, plan.ShareCount, ordinal);
            claims.TryGetValue(ordinal, out var claimState);
            var hasClaim = claims.ContainsKey(ordinal);
            var isAvailable = !hasClaim && IsScopeAvailable(account.Debt.Available, share);
            return new AccountEqualShareSlotSummaryDto(
                ordinal, AccountDebtMath.Total(share), hasClaim ? claimState : null, isAvailable);
        }).ToArray();
        if (slots.Sum(value => value.AmountMinor) != plan.TotalMinor)
            throw new ConflictException("The equal-share plan does not conserve its reviewed total.");
        return new AccountPaymentEqualShareSummaryDto(
            plan.Id,
            plan.AccountRevision,
            plan.TotalMinor,
            plan.ShareCount,
            plan.Currency,
            plan.ActorId == actor.ActorId && plan.ActorKind == actor.Kind,
            slots,
            AccountPaymentSnapshots.ToDtos(scope));
    }

    private async Task<Dictionary<int, AccountPaymentState>> ReadShareClaimsAsync(
        Guid sessionId, Guid planId, int shareCount, CancellationToken cancellationToken)
    {
        var claims = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId && value.EqualSharePlanId == planId
                && value.EqualShareOrdinal != null
                && (value.State == AccountPaymentState.Captured || !NonHoldingStates.Contains(value.State)))
            .Select(value => new { Ordinal = value.EqualShareOrdinal!.Value, value.State })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<int, AccountPaymentState>();
        foreach (var claim in claims)
        {
            if (claim.Ordinal < 1 || claim.Ordinal > shareCount || !result.TryAdd(claim.Ordinal, claim.State))
                throw new ConflictException("The equal-share plan has duplicate or invalid claims to reconcile.");
        }
        return result;
    }

    private static bool IsScopeAvailable(
        IReadOnlyList<AccountDebtSegment> available, IReadOnlyList<AccountDebtSegment> share)
    {
        try
        {
            AccountDebtMath.Subtract(available, share);
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<AccountPaymentAttemptSummaryDto>> ReadActiveAttemptsAsync(
        Guid sessionId, AccountPaymentActor actor, int maximum, CancellationToken cancellationToken)
    {
        var attempts = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId && !NonHoldingStates.Contains(value.State))
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.OperationId)
            .Take(maximum + 1)
            .Select(value => new
            {
                value.OperationId,
                value.ActorId,
                value.ActorKind,
                value.State,
                value.Version,
                value.PaymentMethod,
                value.AmountMinor,
                value.Currency,
                value.ReservationExpiresAt,
                value.EqualSharePlanId,
                value.EqualShareOrdinal
            })
            .ToListAsync(cancellationToken);
        if (attempts.Count > maximum)
            throw new ConflictException("The table account has too many active payment attempts to display safely.");

        return attempts.Select(value =>
        {
            var isOwn = value.ActorId == actor.ActorId && value.ActorKind == actor.Kind;
            return new AccountPaymentAttemptSummaryDto(
                isOwn ? value.OperationId : null,
                value.State,
                value.Version,
                value.PaymentMethod,
                value.AmountMinor,
                value.Currency,
                value.ReservationExpiresAt,
                value.EqualSharePlanId,
                value.EqualShareOrdinal,
                isOwn);
        }).ToArray();
    }
}
