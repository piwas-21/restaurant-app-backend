using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Feature-disabled recovery remains scoped to the original active guest participant.</summary>
public sealed class AccountGuestCheckoutReader(ApplicationDbContext context,
    ITableGuestParticipantPaymentAuthorization authorization, IAccountCheckoutStatusReader statuses)
    : IAccountGuestCheckoutReader
{
    public async Task<AccountCheckoutStartDto> ReadAsync(Guid sessionId, Guid operationId,
        string? participantCredential, CancellationToken cancellationToken)
    {
        await using var snapshot = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var actor = await authorization.AuthorizeActiveAsync(sessionId, participantCredential, cancellationToken);
        var attemptId = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId && value.OperationId == operationId
                && value.ActorId == actor.ActorId && value.ActorKind == actor.Kind)
            .Select(value => value.Id).SingleOrDefaultAsync(cancellationToken);
        if (attemptId == Guid.Empty) throw new NotFoundException("The original guest contribution is unavailable.");
        var dto = await statuses.ReadAsync(attemptId, null, cancellationToken);
        if (snapshot is not null)
            await snapshot.CommitAsync(cancellationToken);
        var currentActor = await authorization.AuthorizeActiveAsync(sessionId, participantCredential, cancellationToken);
        if (currentActor.ActorId != actor.ActorId || currentActor.Kind != actor.Kind)
            throw new NotFoundException("The original guest contribution is unavailable.");
        return dto;
    }
}
