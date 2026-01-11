using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
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
    private readonly ILogger<CompleteAllTableOrdersCommandHandler> _logger;

    public CompleteAllTableOrdersCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<CompleteAllTableOrdersCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
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

            // Fetch all active dine-in orders for this table
            var activeOrders = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.StatusHistory)
                .Where(o => !o.IsDeleted 
                    && o.Type == Domain.Common.Enums.OrderType.DineIn
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
            var userId = _currentUserService.UserId?.ToString() ?? "System";

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
