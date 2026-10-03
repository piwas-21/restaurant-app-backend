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

public sealed class AccountEqualSharePlanService(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITenantFeatures features,
    IOptions<AccountPaymentSettings> options,
    TimeProvider timeProvider) : IAccountEqualSharePlanService
{
    public async Task<AccountEqualSharePlanDto> CreateAsync(
        Guid sessionId, CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken)
    {
        if (!features.TableAccountPaymentsV1)
            throw new NotFoundException("Table account payments are not enabled.");
        if (sessionId == Guid.Empty || request.OperationId == Guid.Empty
            || request.ExpectedAccountRevision <= 0)
            throw new BadRequestException("A valid table visit, operation and account revision are required.");
        if (request.ShareCount < 2 || request.ShareCount > options.Value.MaximumEqualShares)
            throw new BadRequestException("The equal-share count is outside the configured limit.");
        if (request.SupersedesPlanId == Guid.Empty)
            throw new BadRequestException("The superseded plan id is invalid.");

        var actor = actors.ResolveStaffActor();
        var hash = AccountPaymentRequestRules.PlanHash(sessionId, request);
        await using var transaction = await AccountPaymentTransaction.BeginAsync(context, cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table account was not found.");
            await AccountPaymentOperationKeyLock.AcquireAsync(context, request.OperationId, cancellationToken);
            var existing = await context.AccountEqualSharePlans.SingleOrDefaultAsync(
                value => value.OperationId == request.OperationId, cancellationToken);
            if (existing is not null)
            {
                if (existing.ServiceSessionId != sessionId || existing.CreatedBy != actor.AuditIdentifier
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
            if (scope.Count == 0 || total < request.ShareCount)
                throw new BadRequestException("Every reviewed share must contain at least one minor unit.");
            if (scope.Count > options.Value.MaximumScopeSegments)
                throw new BadRequestException("The equal-share scope exceeds the configured segment limit.");

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var plan = new AccountEqualSharePlan
            {
                Id = Guid.NewGuid(),
                ServiceSessionId = sessionId,
                OperationId = request.OperationId,
                AccountRevision = request.ExpectedAccountRevision,
                TotalMinor = total,
                ShareCount = request.ShareCount,
                Currency = account.Money.Currency,
                PayloadHash = hash,
                ScopeJson = AccountPaymentSnapshots.Serialize(scope),
                SupersedesPlanId = request.SupersedesPlanId,
                CreatedAt = now,
                CreatedBy = actor.AuditIdentifier
            };
            if (superseded is not null)
            {
                superseded.InvalidatedAt = now;
                superseded.UpdatedAt = now;
                superseded.UpdatedBy = actor.AuditIdentifier;
            }
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
        if (superseded.CreatedBy != actor.AuditIdentifier)
            throw new ConflictException("The equal-share plan cannot be superseded by this cashier.");

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
}
