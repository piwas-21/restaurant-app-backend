using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;

public record DeleteOrderCommand(Guid OrderId) : ICommand<ApiResponse<bool>>;

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
        // 1. Delete associated TableReservations first to avoid FK constraint violation
        // strict FK "fk_table_reservations_orders_order_id" prevents deleting order otherwise
        // Use IgnoreQueryFilters to ensure we catch ALL linked reservations, even soft-deleted ones
        await _context.TableReservations
            .IgnoreQueryFilters()
            .Where(tr => tr.OrderId == command.OrderId)
            .ExecuteDeleteAsync(cancellationToken);

        // 2. Hard delete the order using ExecuteDeleteAsync for efficiency
        // This will cascade delete related entities (Items, Payments, etc.) due to DB configuration
        var rowsDeleted = await _context.Orders
            .Where(o => o.Id == command.OrderId)
            .ExecuteDeleteAsync(cancellationToken);

        if (rowsDeleted == 0)
        {
            return ApiResponse<bool>.Failure("Order not found");
        }

        _logger.LogInformation(
            "Order with ID {OrderId} permanently deleted by user {UserId}",
            command.OrderId,
            _currentUserService.UserId);

        return ApiResponse<bool>.SuccessWithData(true, "Order permanently deleted");
    }
}
