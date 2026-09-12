using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;

public sealed class ReleaseStaffCounterOrderCommandHandler
    : ICommandHandler<ReleaseStaffCounterOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IStaffOrderOperationStore _operations;
    private readonly IOrderMappingService _mapping;
    private readonly IOrderNotificationService _notifications;

    public ReleaseStaffCounterOrderCommandHandler(
        ApplicationDbContext context, ICurrentUserService currentUser,
        IStaffOrderOperationStore operations, IOrderMappingService mapping,
        IOrderNotificationService notifications)
    {
        _context = context;
        _currentUser = currentUser;
        _operations = operations;
        _mapping = mapping;
        _notifications = notifications;
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        ReleaseStaffCounterOrderCommand command, CancellationToken cancellationToken)
    {
        var hash = StaffOrderOperationFingerprint.CreateRelease(command.OrderId, command.ExpectedVersion);
        var replay = await _operations.ResolveAsync(
            command.ClientOperationId, StaffOrderOperationKind.Release, command.OrderId,
            _currentUser.UserId, hash, cancellationToken);
        if (replay.Outcome != StaffOrderOperationReplayOutcome.None)
        {
            return await ReplayAsync(replay, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var order = await _operations.LoadOrderAsync(command.OrderId, cancellationToken);
            if (order == null)
            {
                return ApiResponse<OrderDto>.Failure("Order not found");
            }

            if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed)
            {
                return ApiResponse<OrderDto>.Failure("A closed order cannot be released to the kitchen.");
            }

            var wasReleased = order.IsKitchenReleased;
            if (!wasReleased && order.Version != command.ExpectedVersion)
            {
                return ApiResponse<OrderDto>.FailureWithCode(
                    "The order changed. Refresh it before releasing it to the kitchen.",
                    ErrorCodes.StaffOrderVersionConflict);
            }

            if (!wasReleased)
            {
                var now = DateTime.UtcNow;
                var previousStatus = order.Status;
                order.IsKitchenReleased = true;
                order.KitchenReleasedAt = now;
                order.KitchenReleasedBy = _currentUser.GetAuditIdentifier();
                if (order.Status == OrderStatus.Pending)
                {
                    order.Status = OrderStatus.Confirmed;
                }
                order.Version++;
                order.UpdatedAt = now;
                order.UpdatedBy = _currentUser.GetAuditIdentifier();
                order.StatusHistory.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    FromStatus = previousStatus,
                    ToStatus = order.Status,
                    Notes = "Staff counter order released to kitchen",
                    ChangedAt = now,
                    ChangedBy = _currentUser.GetAuditIdentifier(),
                    CreatedAt = now,
                    CreatedBy = _currentUser.GetAuditIdentifier()
                });
            }

            _context.StaffOrderOperations.Add(new StaffOrderOperation
            {
                OperationId = command.ClientOperationId,
                OrderId = order.Id,
                Kind = StaffOrderOperationKind.Release,
                ActorUserId = _currentUser.UserId,
                ActorRole = _currentUser.Role?.ToString() ?? string.Empty,
                PayloadHash = hash,
                ExpectedVersion = command.ExpectedVersion,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.GetAuditIdentifier()
            });
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var dto = await _mapping.MapToOrderDtoAsync(order, CancellationToken.None);
            if (!wasReleased)
            {
                await _notifications.NotifyOrderCreatedAsync(dto);
                await _notifications.NotifyFocusOrderUpdateAsync(dto);
            }
            return ApiResponse<OrderDto>.SuccessWithData(
                dto, wasReleased ? "Order was already released" : "Order released to kitchen");
        }
        catch (DbUpdateException ex) when (IsOperationKeyViolation(ex))
        {
            return await ResolveConcurrentAsync(transaction, command, hash, cancellationToken);
        }
        catch (Exception ex) when (IsConcurrencyAbort(ex))
        {
            return await ResolveConcurrentAsync(transaction, command, hash, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<ApiResponse<OrderDto>> ResolveConcurrentAsync(
        IDbContextTransaction transaction,
        ReleaseStaffCounterOrderCommand command,
        string hash,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        _context.ChangeTracker.Clear();

        var winner = await _operations.ResolveAsync(
            command.ClientOperationId, StaffOrderOperationKind.Release, command.OrderId,
            _currentUser.UserId, hash, cancellationToken);
        if (winner.Outcome != StaffOrderOperationReplayOutcome.None)
        {
            return await ReplayAsync(winner, cancellationToken);
        }

        var authoritative = await _operations.LoadOrderAsync(command.OrderId, cancellationToken);
        if (authoritative == null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found");
        }
        if (authoritative.IsKitchenReleased)
        {
            var dto = await _mapping.MapToOrderDtoAsync(authoritative, cancellationToken);
            return ApiResponse<OrderDto>.SuccessWithData(dto, "Order was already released");
        }

        return ApiResponse<OrderDto>.FailureWithCode(
            "The order changed. Refresh it before releasing it to the kitchen.",
            ErrorCodes.StaffOrderVersionConflict);
    }

    private static bool IsConcurrencyAbort(Exception exception) =>
        exception is DbUpdateConcurrencyException || PostgresConcurrencyAborts.IsMatch(exception, out _);

    private async Task<ApiResponse<OrderDto>> ReplayAsync(
        StaffOrderOperationReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Outcome == StaffOrderOperationReplayOutcome.PayloadMismatch)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This release operation was already submitted with different details.",
                ErrorCodes.StaffOrderOperationPayloadMismatch);
        }
        if (replay.Outcome == StaffOrderOperationReplayOutcome.OperationIdReused)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This operation id is already used by another staff operation.",
                ErrorCodes.StaffOrderOperationIdReused);
        }
        if (replay.Order == null)
        {
            return ApiResponse<OrderDto>.Failure("The released order could not be found.");
        }
        var dto = await _mapping.MapToOrderDtoAsync(replay.Order, cancellationToken);
        return ApiResponse<OrderDto>.SuccessWithData(dto, "Order release already recorded");
    }

    private static bool IsOperationKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName?.Contains("operation_id", StringComparison.OrdinalIgnoreCase) == true;
}
