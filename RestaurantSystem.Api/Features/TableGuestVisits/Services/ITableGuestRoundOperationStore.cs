using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

public interface ITableGuestRoundOperationStore
{
    Task<TableGuestRoundPreparation> PrepareUnderLockAsync(
        TableGuestRoundContext context, CancellationToken cancellationToken);

    Task<ApiResponse<OrderDto>?> FindReplayBeforeBasketAsync(
        TableGuestRoundContext context, CancellationToken cancellationToken);

    Task<ApiResponse<OrderDto>?> FindReplayUnderLockAsync(
        TableGuestRoundContext context, TableServiceSession session, CancellationToken cancellationToken);

    Task<TableGuestParticipant> ValidateNewRoundUnderLockAsync(
        TableGuestRoundContext context, TableServiceSession session, CancellationToken cancellationToken);

    TableGuestRoundOperation CreateOperation(
        TableGuestRoundContext context, Guid participantId, Guid orderId, string auditId, DateTime createdAt);
}

public sealed record TableGuestRoundContext(
    Guid ServiceSessionId,
    Guid OperationId,
    long ExpectedAccountRevision,
    string ParticipantTokenHash,
    string BasketSessionHash,
    string ExpectedBasketFingerprint);

public sealed record TableGuestRoundPreparation(
    TableServiceSession Session,
    TableGuestParticipant? Participant,
    ApiResponse<OrderDto>? Replay);
