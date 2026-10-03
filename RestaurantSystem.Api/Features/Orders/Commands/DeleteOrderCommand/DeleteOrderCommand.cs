using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.AccountPayments.Services;
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
        await using var accountMutation = await OrderAccountMutationScope.BeginAsync(
            _context, command.OrderId, cancellationToken);

        var order = await _context.Orders
            .FirstOrDefaultAsync(current => current.Id == command.OrderId, cancellationToken);

        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ApiResponse<bool>.Failure("Order not found");
        }

        ExternalOrderLocalMutationGuard.RequireLocalOrder(order);

        if (command.ExpectedVersion.HasValue && order.Version != command.ExpectedVersion.Value)
        {
            await transaction.RollbackAsync(cancellationToken);
            return VersionConflict();
        }

        await AccountPaymentLedgerGuard.RequireOrderCorrectionAsync(_context, order.Id, cancellationToken);
        var now = DateTime.UtcNow;
        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        var activeReservations = await _context.TableReservations
            .Where(reservation => reservation.OrderId == command.OrderId && reservation.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var reservation in activeReservations)
        {
            reservation.IsActive = false;
            reservation.ReleasedAt = now;
            reservation.ReleasedBy = auditIdentifier;
            reservation.ReleaseReason = "OrderDeleted";
        }

        // ApplicationDbContext converts this into an audited soft delete and increments Version.
        // The order-change trigger records the update while retaining its durable journal rows.
        _context.Orders.Remove(order);
        accountMutation.RecordAccountChange();

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return VersionConflict();
        }

        _logger.LogInformation(
            "Order with ID {OrderId} soft-deleted by user {UserId}",
            command.OrderId,
            _currentUserService.UserId);

        return ApiResponse<bool>.SuccessWithData(true, "Order deleted successfully");
    }

    private static ApiResponse<bool> VersionConflict()
        => ApiResponse<bool>.FailureWithCode(
            "The order changed. Refresh it before deleting.",
            ErrorCodes.OrderVersionConflict);
}
