using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentOperationReader(
    ApplicationDbContext context,
    IAccountPaymentActorResolver actors,
    ITableGuestParticipantPaymentAuthorization guestAuthorization) : IAccountPaymentOperationReader
{
    public async Task<AccountPaymentOperationDto> GetAttemptAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken)
    {
        var actor = actors.ResolveStaffActor();
        var attempt = await context.AccountPaymentAttempts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == sessionId
                && value.OperationId == operationId, cancellationToken);
        if (attempt is null || attempt.ActorId != actor.ActorId || attempt.ActorKind != actor.Kind)
            throw new NotFoundException("The payment operation was not found for this table visit.");
        return AccountPaymentSnapshots.ToOperation(attempt);
    }

    public async Task<AccountPaymentOperationDto> GetGuestAttemptAsync(
        Guid sessionId, Guid operationId, string? participantCredential, CancellationToken cancellationToken)
    {
        var actor = await guestAuthorization.AuthorizeActiveAsync(
            sessionId, participantCredential, cancellationToken);
        var attempt = await context.AccountPaymentAttempts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == sessionId
                && value.OperationId == operationId, cancellationToken);
        var currentActor = await guestAuthorization.AuthorizeActiveAsync(
            sessionId, participantCredential, cancellationToken);
        if (attempt is null || !SameActor(actor, currentActor)
            || attempt.ActorId != actor.ActorId || attempt.ActorKind != actor.Kind)
            throw NotFound();
        return AccountPaymentSnapshots.ToOperation(attempt);
    }

    public async Task<AccountEqualSharePlanDto> GetEqualSharePlanAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken)
    {
        var actor = actors.ResolveStaffActor();
        var plan = await context.AccountEqualSharePlans.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == sessionId
                && value.OperationId == operationId, cancellationToken);
        if (plan is null || !IsOwnedBy(plan, actor))
            throw NotFound();
        return AccountPaymentSnapshots.ToPlan(plan);
    }

    public async Task<AccountEqualSharePlanDto> GetGuestEqualSharePlanAsync(
        Guid sessionId, Guid operationId, string? participantCredential, CancellationToken cancellationToken)
    {
        var actor = await guestAuthorization.AuthorizeActiveAsync(
            sessionId, participantCredential, cancellationToken);
        var plan = await context.AccountEqualSharePlans.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == sessionId
                && value.OperationId == operationId, cancellationToken);
        var currentActor = await guestAuthorization.AuthorizeActiveAsync(
            sessionId, participantCredential, cancellationToken);
        if (plan is null || !SameActor(actor, currentActor) || !IsOwnedBy(plan, actor))
            throw NotFound();
        return AccountPaymentSnapshots.ToPlan(plan);
    }

    private static bool IsOwnedBy(AccountEqualSharePlan plan, AccountPaymentActor actor) =>
        plan.ActorId == actor.ActorId && plan.ActorKind == actor.Kind;

    private static bool SameActor(AccountPaymentActor first, AccountPaymentActor second) =>
        first.ActorId == second.ActorId && first.Kind == second.Kind;

    private static NotFoundException NotFound() =>
        new("The payment operation was not found for this table visit.");
}
