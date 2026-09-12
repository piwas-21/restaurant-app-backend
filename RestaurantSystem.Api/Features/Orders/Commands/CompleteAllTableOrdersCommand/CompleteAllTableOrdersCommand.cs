using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.CompleteAllTableOrdersCommand;

public record CompleteAllTableOrdersCommand(string TableNumber) : ICommand<ApiResponse<CompleteAllTableOrdersResult>>;

public class CompleteAllTableOrdersResult
{
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public int TotalProcessed { get; set; }
    public List<string> ProcessedOrderNumbers { get; set; } = new();
}

public class CompleteAllTableOrdersCommandHandler : ICommandHandler<CompleteAllTableOrdersCommand, ApiResponse<CompleteAllTableOrdersResult>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITableBillTargetResolver _targetResolver;
    private readonly ILogger<CompleteAllTableOrdersCommandHandler> _logger;

    public CompleteAllTableOrdersCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<CompleteAllTableOrdersCommandHandler> logger,
        ITableBillTargetResolver? targetResolver = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _targetResolver = targetResolver ?? new TableBillTargetResolver(context);
        _logger = logger;
    }

    public async Task<ApiResponse<CompleteAllTableOrdersResult>> Handle(
        CompleteAllTableOrdersCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse table number
            if (!int.TryParse(request.TableNumber, out var tableNumberInt))
            {
                return ApiResponse<CompleteAllTableOrdersResult>.Failure("Invalid table number");
            }

            // The legacy route has only a table number, so it cannot know which visit owns an
            // order. Refuse both an active explicit visit and any ambiguous explicit membership
            // before loading rows; otherwise clearing one visit could cancel another visit's work.
            var target = await _targetResolver.ResolveAsync(tableNumberInt, cancellationToken);
            if (target.IsAmbiguous || target.ServiceSessionId.HasValue)
            {
                var reason = target.Reason
                    ?? $"Table {request.TableNumber} has an active service session. "
                    + "Complete it through the explicit service-session endpoint.";
                _logger.LogInformation(
                    "Refusing legacy table clear for table {TableNumber}: {Reason}",
                    request.TableNumber, reason);
                return ApiResponse<CompleteAllTableOrdersResult>.FailureWithCode(
                    reason, ErrorCodes.TableServiceSessionAmbiguous);
            }

            // Fetch all active dine-in orders for this table
            var activeOrders = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.StatusHistory)
                .Where(o => !o.IsDeleted
                    && o.Type == OrderType.DineIn
                    && o.TableNumber == tableNumberInt
                    && o.Status != OrderStatus.Completed
                    && o.Status != OrderStatus.Cancelled)
                .ToListAsync(cancellationToken);

            if (!activeOrders.Any())
            {
                return ApiResponse<CompleteAllTableOrdersResult>.SuccessWithData(
                    new CompleteAllTableOrdersResult(),
                    "No active orders found for this table"
                );
            }

            var result = new CompleteAllTableOrdersResult();
            var now = DateTime.UtcNow;
            var userId = _currentUserService.GetAuditIdentifier();

            foreach (var order in activeOrders)
            {
                OrderStatus newStatus;
                string notes;

                // Determine the appropriate terminal status based on current status
                if (order.Status == OrderStatus.Ready)
                {
                    // Order is ready, mark as completed (served)
                    newStatus = OrderStatus.Completed;
                    notes = "Table cleared - order marked as completed by server";
                    order.ActualDeliveryTime = now;
                    result.CompletedCount++;
                }
                else
                {
                    // Order not ready yet (Pending, Confirmed, Preparing), cancel it
                    newStatus = OrderStatus.Cancelled;
                    notes = "Table cleared - order cancelled as it was not served";
                    result.CancelledCount++;
                }

                // Add status history
                var statusHistory = new Domain.Entities.OrderStatusHistory
                {
                    OrderId = order.Id,
                    FromStatus = order.Status,
                    ToStatus = newStatus,
                    Notes = notes,
                    ChangedAt = now,
                    ChangedBy = userId,
                    CreatedAt = now,
                    CreatedBy = userId
                };

                _context.OrderStatusHistories.Add(statusHistory);

                // Update order status
                order.Status = newStatus;
                order.UpdatedAt = now;
                order.UpdatedBy = userId;

                result.ProcessedOrderNumbers.Add(order.OrderNumber);
                result.TotalProcessed++;

                _logger.LogInformation(
                    "Order {OrderNumber} on table {TableNumber} transitioned from {FromStatus} to {ToStatus}",
                    order.OrderNumber, request.TableNumber, statusHistory.FromStatus, newStatus);
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Completed all orders for table {TableNumber}. Completed: {Completed}, Cancelled: {Cancelled}",
                request.TableNumber, result.CompletedCount, result.CancelledCount);

            return ApiResponse<CompleteAllTableOrdersResult>.SuccessWithData(
                result,
                $"Processed {result.TotalProcessed} orders: {result.CompletedCount} completed, {result.CancelledCount} cancelled"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing all orders for table {TableNumber}", request.TableNumber);
            return ApiResponse<CompleteAllTableOrdersResult>.Failure("Failed to complete table orders");
        }
    }
}
