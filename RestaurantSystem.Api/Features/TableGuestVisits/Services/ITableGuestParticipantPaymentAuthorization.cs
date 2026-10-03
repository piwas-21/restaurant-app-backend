using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public interface ITableGuestParticipantPaymentAuthorization
{
    Task<AccountPaymentActor> AuthorizeLockedAsync(
        TableServiceSession lockedSession,
        string? participantCredential,
        CancellationToken cancellationToken);

    Task<AccountPaymentActor> AuthorizeActiveAsync(
        Guid serviceSessionId,
        string? participantCredential,
        CancellationToken cancellationToken);
}
