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

public sealed class AccountEqualSharePlanService(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITableGuestParticipantPaymentAuthorization guestAuthorization,
    IGuestAccountPaymentPolicy guestPolicy,
    ITenantFeatures features,
    IOptions<AccountPaymentSettings> options,
    TimeProvider timeProvider) : IAccountEqualSharePlanService
{
    public async Task<AccountEqualSharePlanDto> CreateAsync(
        Guid sessionId, CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken)
    {
        ValidateCreateRequest(sessionId, request);
        actors.RequireNewCollection();

        var actor = actors.ResolveStaffActor();
        return await CreateCoreAsync(sessionId, null, request, actor, guest: false, cancellationToken);
    }

    public async Task<AccountEqualSharePlanDto> CreateGuestAsync(
        Guid sessionId,
        string? participantCredential,
        CreateAccountEqualSharePlanRequest request,
        CancellationToken cancellationToken)
    {
        guestPolicy.RequireNewPayment();
        ValidateCreateRequest(sessionId, request);
        return await CreateCoreAsync(sessionId, participantCredential, request, null, guest: true, cancellationToken);
    }

    public async Task<AccountSharePlanReviewDto> ReviewGuestAsync(Guid sessionId, string? participantCredential,
        CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken)
    {
        guestPolicy.RequireNewPayment();
        ValidateCreateRequest(sessionId, request);
        var actor = await guestAuthorization.AuthorizeActiveAsync(sessionId, participantCredential, cancellationToken);
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
        if (account.Session.Status != TableServiceSessionStatus.Open || account.Session.ReleasedAt.HasValue
            || !account.Session.TableId.HasValue || account.Session.BillingAllocationVersion is < 0 or > 1)
            throw new ConflictException("Refresh the open table account before reviewing a split.");
        AccountPaymentRequestRules.RequireCurrentRevision(account.Session.AccountRevision, request.ExpectedAccountRevision);
        await FindSupersededPlanAsync(sessionId, request.SupersedesPlanId, actor, cancellationToken);
        var scope = account.Debt.Available;
        var total = AccountDebtMath.Total(scope);
        var increment = request.CustomAmountsMinor.Count == 0
            ? AccountShareMath.EqualShareIncrementMinorUnits(account.Money.Currency) : 1;
        AccountSharePlanRules.RequireScope(scope, total, request, increment, options.Value);
        var shares = request.CustomAmountsMinor.Count == 0
            ? Enumerable.Range(1, request.ShareCount).Select(AccountShareMath.Equal(total, request.ShareCount, increment).At).ToArray()
            : request.CustomAmountsMinor.ToArray();
        var review = new AccountSharePlanReviewDto(account.Session.TableId.Value, total, account.Money.Currency, checked((int)increment),
            AccountPaymentSnapshots.ToDtos(scope), shares);
        RequireOnlineShares(review);
        return review;
    }

    public void RequireOnlineShares(AccountSharePlanReviewDto review)
    {
        foreach (var share in review.SharesMinor) guestPolicy.RequireContribution(share, review.Currency);
    }

    private async Task<AccountEqualSharePlanDto> CreateCoreAsync(
        Guid sessionId,
        string? participantCredential,
        CreateAccountEqualSharePlanRequest request,
        AccountPaymentActor? staffActor,
        bool guest,
        CancellationToken cancellationToken)
    {
        var hash = AccountPaymentRequestRules.PlanHash(sessionId, request);
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            var actor = guest
                ? await guestAuthorization.AuthorizeLockedAsync(session, participantCredential, cancellationToken)
                : staffActor!;
            await AccountPaymentOperationKeyLock.AcquireAsync(context, request.OperationId, cancellationToken);
            var existing = await context.AccountEqualSharePlans.SingleOrDefaultAsync(
                value => value.OperationId == request.OperationId, cancellationToken);
            if (existing is not null)
            {
                if (existing.ServiceSessionId != sessionId || !IsOwnedBy(existing, actor)
                    || existing.PayloadHash != hash)
                    throw new ConflictException("The operation id has already been used.");
                await transaction.CommitAsync(cancellationToken);
                return AccountPaymentSnapshots.ToPlan(existing);
            }
            if (await context.AccountPaymentAttempts.AnyAsync(
                    value => value.OperationId == request.OperationId, cancellationToken))
                throw new ConflictException("The operation id has already been used.");

            if (session.Status != TableServiceSessionStatus.Open)
                throw new ConflictException("A closed table visit cannot accept an equal-share plan.");
            if (string.IsNullOrWhiteSpace(session.Currency))
                throw new ConflictException("The table visit has no declared currency for payment quotes.");
            AccountPaymentRequestRules.RequireCurrentRevision(session.AccountRevision, request.ExpectedAccountRevision);
            var superseded = await FindSupersededPlanAsync(
                sessionId, request.SupersedesPlanId, actor, cancellationToken);
            var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
            var scope = account.Debt.Available;
            var total = AccountDebtMath.Total(scope);
            var roundingIncrement = request.CustomAmountsMinor.Count == 0
                ? AccountShareMath.EqualShareIncrementMinorUnits(account.Money.Currency)
                : 1;
            AccountSharePlanRules.RequireScope(scope, total, request, roundingIncrement, options.Value);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var plan = CreatePlan(new PlanCreationData(
                sessionId, request, account, total, roundingIncrement, hash, now, actor));
            InvalidateSupersededPlan(superseded, now, actor);
            context.AccountEqualSharePlans.Add(plan);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountPaymentSnapshots.ToPlan(plan);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException("The account changed while the equal-share plan was created.", exception);
        }
        catch (DbUpdateException exception) when (AccountPaymentWriteErrors.IsOperationKeyConflict(exception))
        {
            throw AccountPaymentWriteErrors.OperationKeyConflict(exception);
        }
    }

    private void ValidateCreateRequest(Guid sessionId, CreateAccountEqualSharePlanRequest request)
    {
        if (!features.TableAccountPaymentsV1)
            throw new NotFoundException("Table account payments are not enabled.");
        AccountSharePlanRules.RequireRequest(sessionId, request, options.Value);
    }

    private static AccountEqualSharePlan CreatePlan(PlanCreationData data) => new()
    {
        Id = Guid.NewGuid(),
        ServiceSessionId = data.SessionId,
        OperationId = data.Request.OperationId,
        AccountRevision = data.Request.ExpectedAccountRevision,
        TotalMinor = data.Total,
        ShareCount = data.Request.ShareCount,
        RoundingIncrementMinor = checked((int)data.RoundingIncrement),
        Currency = data.Account.Money.Currency,
        PayloadHash = data.Hash,
        ScopeJson = AccountPaymentSnapshots.Serialize(data.Account.Debt.Available),
        CustomAmountsJson = data.Request.CustomAmountsMinor.Count > 0
                ? AccountPaymentSnapshots.Serialize(data.Request.CustomAmountsMinor)
                : null,
        ActorId = data.Actor.ActorId,
        ActorKind = data.Actor.Kind,
        SupersedesPlanId = data.Request.SupersedesPlanId,
        CreatedAt = data.CreatedAt,
        CreatedBy = data.Actor.AuditIdentifier
    };

    private sealed record PlanCreationData(
        Guid SessionId,
        CreateAccountEqualSharePlanRequest Request,
        AccountPaymentAccountSnapshot Account,
        long Total,
        long RoundingIncrement,
        string Hash,
        DateTime CreatedAt,
        AccountPaymentActor Actor);

    private static void InvalidateSupersededPlan(
        AccountEqualSharePlan? superseded, DateTime now, AccountPaymentActor actor)
    {
        if (superseded is null)
            return;
        superseded.InvalidatedAt = now;
        superseded.UpdatedAt = now;
        superseded.UpdatedBy = actor.AuditIdentifier;
    }

    private async Task<AccountEqualSharePlan?> FindSupersededPlanAsync(
        Guid sessionId,
        Guid? supersedesPlanId,
        AccountPaymentActor actor,
        CancellationToken cancellationToken)
    {
        var activePlans = await context.AccountEqualSharePlans
            .Where(value => value.ServiceSessionId == sessionId && value.InvalidatedAt == null)
            .ToListAsync(cancellationToken);
        if (activePlans.Count == 0)
        {
            if (supersedesPlanId.HasValue)
                throw new ConflictException("There is no active equal-share plan to supersede.");
            return null;
        }

        if (activePlans.Count != 1 || supersedesPlanId != activePlans[0].Id)
            throw new ConflictException("Review and explicitly supersede the active equal-share plan first.");

        var superseded = activePlans[0];
        if (!IsOwnedBy(superseded, actor))
            throw new ConflictException("The equal-share plan cannot be superseded by this account participant.");

        var knownUnprotected = AccountPaymentStateRules.KnownUnprotectedStates;
        if (await context.AccountPaymentAttempts.AnyAsync(attempt =>
                attempt.EqualSharePlanId == superseded.Id && !knownUnprotected.Contains(attempt.State),
                cancellationToken))
        {
            throw new ConflictException(
                "A plan with captured or provider-pending contributions cannot be superseded.");
        }

        return superseded;
    }

    private static bool IsOwnedBy(AccountEqualSharePlan plan, AccountPaymentActor actor) =>
        plan.ActorId == actor.ActorId && plan.ActorKind == actor.Kind;
}
