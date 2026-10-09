using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public sealed class TableGuestParticipantPaymentAuthorization(
    ApplicationDbContext context,
    TimeProvider timeProvider) : ITableGuestParticipantPaymentAuthorization
{
    private const string UnavailableMessage = "Guest access is unavailable for this table visit.";

    public async Task<AccountPaymentActor> AuthorizeLockedAsync(
        TableServiceSession lockedSession,
        string? participantCredential,
        CancellationToken cancellationToken)
    {
        if (lockedSession.Status != TableServiceSessionStatus.Open || lockedSession.ReleasedAt.HasValue
            || !lockedSession.TableId.HasValue)
            throw Unavailable();

        var participant = await FindParticipantAsync(
            lockedSession.Id, participantCredential, cancellationToken);
        return ToActor(participant);
    }

    public async Task<AccountPaymentActor> AuthorizeActiveAsync(
        Guid serviceSessionId,
        string? participantCredential,
        CancellationToken cancellationToken)
    {
        var session = await context.TableServiceSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
        if (session?.Status != TableServiceSessionStatus.Open || session.ReleasedAt.HasValue
            || !session.TableId.HasValue)
            throw Unavailable();

        var participant = await FindParticipantAsync(serviceSessionId, participantCredential, cancellationToken);
        return ToActor(participant);
    }

    private async Task<TableGuestParticipant> FindParticipantAsync(
        Guid serviceSessionId,
        string? credential,
        CancellationToken cancellationToken)
    {
        if (serviceSessionId == Guid.Empty
            || !TableGuestCredentialCrypto.TryHashParticipantToken(credential, out var tokenHash))
        {
            throw Unavailable();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var participant = await context.Set<TableGuestParticipant>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == serviceSessionId
                && value.TokenHash == tokenHash
                && value.RevokedAt == null
                && value.ExpiresAt > now,
                cancellationToken);
        return participant ?? throw Unavailable();
    }

    private static AccountPaymentActor ToActor(TableGuestParticipant participant) => new(
        participant.Id,
        AccountPaymentActorKind.GuestParticipant,
        $"GuestParticipant:{participant.Id:N}",
        null);

    private static NotFoundException Unavailable() => new(UnavailableMessage);
}
