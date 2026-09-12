using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;

public record DeleteOrderCommand(Guid OrderId) : ICommand<ApiResponse<bool>>
{
    /// <summary>Optional detail version; older administrative clients may omit it.</summary>
    public int? ExpectedVersion { get; init; }
}

public class DeleteOrderCommandHandler : ICommandHandler<DeleteOrderCommand, ApiResponse<bool>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteOrderCommandHandler> _logger;

    public DeleteOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<DeleteOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteOrderCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var order = await _context.Orders
            .Where(order => order.Id == command.OrderId)
            .Select(order => new { order.Version })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ApiResponse<bool>.Failure("Order not found");
        }

        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ApiResponse<bool>.FailureWithCode(
                "The order changed. Refresh it before deleting.",
                ErrorCodes.OrderVersionConflict);
        }

        // Delete associated TableReservations first to avoid the restrict FK. Keep this in the
        // same transaction as the conditional order delete so a stale version cannot remove a
        // reservation before discovering that the order changed.
        await _context.TableReservations
            // soft-delete-bypass: permanent order purge must also remove linked reservations,
            // including already soft-deleted rows hidden by the global filter, before the restrict FK.
            .IgnoreQueryFilters()
            .Where(reservation => reservation.OrderId == command.OrderId)
            .ExecuteDeleteAsync(cancellationToken);

        var orderQuery = _context.Orders.Where(current => current.Id == command.OrderId);
        if (command.ExpectedVersion.HasValue)
        {
            orderQuery = orderQuery.Where(current => current.Version == command.ExpectedVersion.Value);
        }

        var rowsDeleted = await orderQuery.ExecuteDeleteAsync(cancellationToken);
        if (rowsDeleted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var stillExists = await _context.Orders
                .AnyAsync(current => current.Id == command.OrderId, cancellationToken);
            return stillExists && command.ExpectedVersion.HasValue
                ? ApiResponse<bool>.FailureWithCode(
                    "The order changed. Refresh it before deleting.",
                    ErrorCodes.OrderVersionConflict)
                : ApiResponse<bool>.Failure("Order not found");
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Order with ID {OrderId} permanently deleted by user {UserId}",
            command.OrderId,
            _currentUserService.UserId);

        return ApiResponse<bool>.SuccessWithData(true, "Order permanently deleted");
    }
}
