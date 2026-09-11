using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;

public sealed class CreateStaffCounterOrderCommandHandler
    : ICommandHandler<CreateStaffCounterOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IStaffCounterOrderBuilder _builder;
    private readonly IStaffOrderOperationStore _operations;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderNotificationService _notifications;
    private readonly IOrderTableReservationService _tableReservation;

    [SuppressMessage("Maintainability", "S107:Methods should not have too many parameters",
        Justification = "The handler composes transaction, identity, idempotency, pricing, fidelity, mapping, live events, and table reservation services; wrapping DI-only dependencies would hide the transaction boundary without reducing responsibility.")]
    public CreateStaffCounterOrderCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUser,
        IStaffCounterOrderBuilder builder, IStaffOrderOperationStore operations,
        IOrderMappingService mapping, IOrderFidelityCoordinator fidelity,
        IOrderNotificationService notifications, IOrderTableReservationService tableReservation)
    {
        _context = context;
        _currentUser = currentUser;
        _builder = builder;
        _operations = operations;
        _mapping = mapping;
        _fidelity = fidelity;
        _notifications = notifications;
        _tableReservation = tableReservation;
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        CreateStaffCounterOrderCommand command, CancellationToken cancellationToken)
    {
        // Keep this guard ahead of the operation ledger and transaction as a defense for callers that
        // invoke the handler without the mediator validation behavior.
        if (command.PointsToRedeem is > 0)
        {
            throw new BadRequestException("Points redemption is not supported for staff counter orders.");
        }

        var hash = StaffOrderOperationFingerprint.Create(command, command.ReleaseToKitchen);
        var replay = await _operations.ResolveAsync(
            command.ClientOperationId, StaffOrderOperationKind.Create, null,
            _currentUser.UserId, hash, cancellationToken);
        if (replay.Outcome != StaffOrderOperationReplayOutcome.None)
        {
            return await ReplayAsync(replay, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var build = await _builder.BuildAsync(command, command.ReleaseToKitchen, cancellationToken);
            _context.Orders.Add(build.Order);
            _context.StaffOrderOperations.Add(new StaffOrderOperation
            {
                OperationId = command.ClientOperationId,
                OrderId = build.Order.Id,
                Kind = StaffOrderOperationKind.Create,
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

            return await PublishAsync(build.Order, "Counter order created", cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOperationKeyViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
            var winner = await _operations.ResolveAsync(
                command.ClientOperationId, StaffOrderOperationKind.Create, null,
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
                "This operation was already submitted with different order details.",
                ErrorCodes.StaffOrderOperationPayloadMismatch);
        }

        if (replay.Outcome == StaffOrderOperationReplayOutcome.OperationIdReused)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This operation id is already used by another staff order.",
                ErrorCodes.StaffOrderOperationIdReused);
        }

        if (replay.Order == null)
        {
            return ApiResponse<OrderDto>.Failure("The original counter order could not be found.");
        }

        var dto = await _mapping.MapToOrderDtoAsync(replay.Order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(dto, "Counter order already created");
    }

    private async Task<ApiResponse<OrderDto>> PublishAsync(
        Order order, string message, CancellationToken cancellationToken)
    {
        var dto = await _mapping.MapToOrderDtoAsync(order, cancellationToken);
        if (order.IsKitchenReleased)
        {
            await _notifications.NotifyOrderCreatedAsync(dto);
        }
        await _notifications.NotifyFocusOrderUpdateAsync(dto);
        await _tableReservation.ReserveForDineInAsync(order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(dto, message);
    }

    private static bool IsOperationKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName?.Contains("operation_id", StringComparison.OrdinalIgnoreCase) == true;
}
