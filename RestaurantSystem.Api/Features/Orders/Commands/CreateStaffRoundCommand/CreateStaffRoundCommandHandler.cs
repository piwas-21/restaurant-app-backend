using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffRoundCommand;

public sealed class CreateStaffRoundCommandHandler
    : ICommandHandler<CreateStaffRoundCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IStaffCounterOrderBuilder _builder;
    private readonly IStaffOrderOperationStore _operations;
    private readonly IOrderResponseProjector _responses;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderNotificationService _notifications;
    private readonly IOrderTableReservationService _tableReservation;
    private readonly IOrderRoutingService _routing;

    [SuppressMessage("Maintainability", "S107:Methods should not have too many parameters",
        Justification = "This is the existing authenticated staff order pipeline with a session lock added at its transaction boundary.")]
    public CreateStaffRoundCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUser,
        IStaffCounterOrderBuilder builder, IStaffOrderOperationStore operations,
        IOrderResponseProjector responses, IOrderFidelityCoordinator fidelity,
        IOrderNotificationService notifications, IOrderTableReservationService tableReservation,
        IOrderRoutingService routing)
    {
        _context = context;
        _currentUser = currentUser;
        _builder = builder;
        _operations = operations;
        _responses = responses;
        _fidelity = fidelity;
        _notifications = notifications;
        _tableReservation = tableReservation;
        _routing = routing;
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        CreateStaffRoundCommand command, CancellationToken cancellationToken)
    {
        if (command.Type != OrderType.DineIn || command.DeliveryAddress is not null)
        {
            throw new BadRequestException("A staff round must be a dine-in order without a delivery address.");
        }

        if (command.PointsToRedeem is > 0)
        {
            throw new BadRequestException("Points redemption is not supported for staff rounds.");
        }

        if (!command.ServiceSessionId.HasValue)
        {
            throw new BadRequestException(
                "A dine-in staff round requires an open table service session.",
                ErrorCodes.TableServiceSessionRequired);
        }

        var hash = StaffOrderOperationFingerprint.Create(command, command.ReleaseToKitchen);
        var replay = await _operations.ResolveAsync(
            command.ClientOperationId, StaffOrderOperationKind.RoundCreate, null,
            _currentUser.UserId, hash, cancellationToken);
        if (replay.Outcome != StaffOrderOperationReplayOutcome.None)
        {
            return await ReplayAsync(replay, cancellationToken);
        }

        // The session row lock is the serialization point shared with close. Read committed keeps
        // a second waiter from receiving a false PostgreSQL serialization abort after it waits for
        // the first round to commit; it then reads the committed session state under that lock.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var session = await TableServiceSessionRowLock.LoadAsync(
                _context, command.ServiceSessionId.Value, cancellationToken);
            if (session is null)
            {
                return ApiResponse<OrderDto>.FailureWithCode(
                    "The open table service session was not found.",
                    ErrorCodes.TableServiceSessionNotFound);
            }

            if (session.Status != TableServiceSessionStatus.Open)
            {
                return ApiResponse<OrderDto>.FailureWithCode(
                    "The table service session is no longer open.",
                    ErrorCodes.TableServiceSessionStale);
            }

            var build = await _builder.BuildAsync(command, command.ReleaseToKitchen, cancellationToken);
            _context.Orders.Add(build.Order);
            if (command.ReleaseToKitchen)
            {
                await _routing.EnsureRoutesAsync(build.Order, cancellationToken);
            }

            _context.StaffOrderOperations.Add(new StaffOrderOperation
            {
                OperationId = command.ClientOperationId,
                OrderId = build.Order.Id,
                Kind = StaffOrderOperationKind.RoundCreate,
                ActorUserId = _currentUser.UserId,
                ActorRole = _currentUser.Role?.ToString() ?? string.Empty,
                PayloadHash = hash,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.GetAuditIdentifier()
            });
            await _context.SaveChangesAsync(cancellationToken);
            await _fidelity.RedeemAsync(
                build.Order, command.PointsToRedeem, build.CustomerUserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var dto = await _responses.ProjectAsync(build.Order, CancellationToken.None);
            if (build.Order.IsKitchenReleased)
            {
                await _notifications.NotifyOrderCreatedAsync(dto);
            }

            await _notifications.NotifyFocusOrderUpdateAsync(dto);
            await _tableReservation.ReserveForDineInAsync(build.Order, CancellationToken.None);
            return ApiResponse<OrderDto>.SuccessWithData(
                dto, "Staff round created");
        }
        catch (DbUpdateException exception) when (IsOperationKeyViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            var winner = await _operations.ResolveAsync(
                command.ClientOperationId, StaffOrderOperationKind.RoundCreate, null,
                _currentUser.UserId, hash, cancellationToken);
            return await ReplayAsync(winner, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<ApiResponse<OrderDto>> ReplayAsync(
        StaffOrderOperationReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Outcome == StaffOrderOperationReplayOutcome.PayloadMismatch)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This round operation was already submitted with different order details.",
                ErrorCodes.StaffOrderOperationPayloadMismatch);
        }

        if (replay.Outcome == StaffOrderOperationReplayOutcome.OperationIdReused)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This operation id is already used by another staff order.",
                ErrorCodes.StaffOrderOperationIdReused);
        }

        if (replay.Outcome == StaffOrderOperationReplayOutcome.Unknown || replay.Order is null)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The round operation is unknown.", ErrorCodes.StaffOrderOperationUnknown);
        }

        var dto = await _responses.ProjectAsync(replay.Order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(dto, "Staff round already created");
    }

    private static bool IsOperationKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName?.Contains("operation_id", StringComparison.OrdinalIgnoreCase) == true;
}
