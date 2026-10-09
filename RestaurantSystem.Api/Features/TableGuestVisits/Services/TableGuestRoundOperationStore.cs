using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services;

internal sealed class TableGuestRoundOperationStore : ITableGuestRoundOperationStore
{
    private const string UnavailableMessage = "Guest access is unavailable for this table visit.";
    private readonly ApplicationDbContext _context;
    private readonly ITenantFeatures _features;
    private readonly IOrderResponseProjector _responses;
    private readonly TimeProvider _timeProvider;

    public TableGuestRoundOperationStore(
        ApplicationDbContext context,
        ITenantFeatures features,
        IOrderResponseProjector responses,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _features = features;
        _responses = responses;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<OrderDto>?> FindReplayBeforeBasketAsync(
        TableGuestRoundContext context, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var preparation = await PrepareUnderLockAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return preparation.Replay;
    }

    public async Task<TableGuestRoundPreparation> PrepareUnderLockAsync(
        TableGuestRoundContext context, CancellationToken cancellationToken)
    {
        var session = await TableServiceSessionRowLock.LoadAsync(
            _context, context.ServiceSessionId, cancellationToken);
        if (session is null)
        {
            throw Unavailable();
        }

        var replay = await FindReplayUnderLockAsync(context, session, cancellationToken);
        var participant = replay is null
            ? await ValidateNewRoundUnderLockAsync(context, session, cancellationToken)
            : null;
        return new TableGuestRoundPreparation(session, participant, replay);
    }

    public async Task<ApiResponse<OrderDto>?> FindReplayUnderLockAsync(
        TableGuestRoundContext context, TableServiceSession session, CancellationToken cancellationToken)
    {
        var participant = await FindActiveParticipantAsync(context, session, cancellationToken);
        var requestHash = TableGuestCredentialCrypto.HashRoundRequest(
            session.Id, participant.Id, context.OperationId, context.ExpectedAccountRevision,
            context.BasketSessionHash, context.ExpectedBasketFingerprint);
        var operation = await _context.Set<TableGuestRoundOperation>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ServiceSessionId == session.Id
                && value.OperationId == context.OperationId, cancellationToken);
        if (operation is null)
        {
            return null;
        }

        if (operation.ParticipantId != participant.Id)
        {
            throw Unavailable();
        }

        if (!string.Equals(operation.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new BadRequestException(
                "This round operation id has already been used with different request details.",
                ErrorCodes.TableServiceSessionStale);
        }

        var order = await _context.Orders
            .IncludeOrderLineGraph()
            .Include(value => value.Payments)
            .Include(value => value.StatusHistory)
            .Include(value => value.DeliveryAddress)
            .Include(value => value.RoutingStates)
            .AsSplitQuery()
            .SingleOrDefaultAsync(value => value.Id == operation.OrderId && !value.IsDeleted, cancellationToken);
        if (order is null || order.ServiceSessionId != session.Id)
        {
            throw Unavailable();
        }

        var dto = await _responses.ProjectAsync(order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(dto, "Guest round already created");
    }

    public async Task<TableGuestParticipant> ValidateNewRoundUnderLockAsync(
        TableGuestRoundContext context, TableServiceSession session, CancellationToken cancellationToken)
    {
        var participant = await FindActiveParticipantAsync(context, session, cancellationToken);
        if (session.AccountRevision != context.ExpectedAccountRevision)
        {
            throw new BadRequestException(
                "The table account changed. Refresh it before adding another round.",
                ErrorCodes.TableServiceSessionStale);
        }

        return participant;
    }

    public TableGuestRoundOperation CreateOperation(
        TableGuestRoundContext context, Guid participantId, Guid orderId, string auditId, DateTime createdAt)
    {
        var hash = TableGuestCredentialCrypto.HashRoundRequest(
            context.ServiceSessionId, participantId, context.OperationId,
            context.ExpectedAccountRevision, context.BasketSessionHash, context.ExpectedBasketFingerprint);
        return new TableGuestRoundOperation
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = context.ServiceSessionId,
            ParticipantId = participantId,
            OperationId = context.OperationId,
            OrderId = orderId,
            RequestHash = hash,
            CreatedAt = createdAt,
            CreatedBy = auditId,
        };
    }

    private async Task<TableGuestParticipant> FindActiveParticipantAsync(
        TableGuestRoundContext context, TableServiceSession session, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (session.Id != context.ServiceSessionId || session.Status != TableServiceSessionStatus.Open
            || session.ReleasedAt.HasValue
            || !session.TableId.HasValue || context.OperationId == Guid.Empty
            || context.ExpectedAccountRevision < 1 || context.ParticipantTokenHash.Length != 64
            || !BasketPurchaseFingerprint.IsValidDigest(context.ExpectedBasketFingerprint))
        {
            throw Unavailable();
        }

        var participant = await _context.Set<TableGuestParticipant>().SingleOrDefaultAsync(value =>
            value.ServiceSessionId == session.Id
            && value.TokenHash == context.ParticipantTokenHash
            && value.RevokedAt == null
            && value.ExpiresAt > now,
            cancellationToken);
        return participant ?? throw Unavailable();
    }

    private void EnsureEnabled()
    {
        if (!_features.TableGuestVisitsV1)
        {
            throw Unavailable();
        }
    }

    private static NotFoundException Unavailable() => new(UnavailableMessage);
}
