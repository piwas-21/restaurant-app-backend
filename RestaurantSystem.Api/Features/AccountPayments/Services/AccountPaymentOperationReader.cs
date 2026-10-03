using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountPaymentOperationReader(
    ApplicationDbContext context, IAccountPaymentActorResolver actors) : IAccountPaymentOperationReader
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

    public async Task<AccountEqualSharePlanDto> GetEqualSharePlanAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken)
    {
        var actor = actors.ResolveStaffActor();
        var plan = await context.AccountEqualSharePlans.AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == sessionId
                && value.OperationId == operationId, cancellationToken);
        if (plan is null || plan.CreatedBy != actor.AuditIdentifier)
            throw new NotFoundException("The equal-share plan was not found for this table visit.");
        return AccountPaymentSnapshots.ToPlan(plan);
    }
}
