using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Commands.ToggleFocusOrderCommand;

public record ToggleFocusOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
    public bool IsFocusOrder { get; set; }
    public int? Priority { get; set; }
    public string? FocusReason { get; set; }
}
public class ToggleFocusOrderCommandHandler : ICommandHandler<ToggleFocusOrderCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderEventService _orderEventService;
    private readonly ILogger<ToggleFocusOrderCommandHandler> _logger;
    private readonly IOrderMappingService _mappingService;


    public ToggleFocusOrderCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderEventService orderEventService,
        IOrderMappingService mappingService,
        ILogger<ToggleFocusOrderCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderEventService = orderEventService;
        _mappingService = mappingService;
        _logger = logger;
    }


    public async Task<ApiResponse<OrderDto>> Handle(ToggleFocusOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId && !o.IsDeleted, cancellationToken);

        if (order == null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found");
        }

        // Un-focusing is now dropping the record rather than clearing four columns one by one,
        // so it cannot leave a stale FocusedBy or FocusReason behind.
        order.Focus = command.IsFocusOrder
            ? new OrderFocus
            {
                Priority = command.Priority ?? 3, // Default priority
                Reason = command.FocusReason,
                FocusedAt = DateTime.UtcNow,
                FocusedBy = _currentUserService.UserId?.ToString()
            }
            : null;

        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        await _context.SaveChangesAsync(cancellationToken);

        var orderDto = await _mappingService.MapToOrderDtoAsync(order, cancellationToken);

        await _orderEventService.NotifyFocusOrderUpdate(orderDto);

        _logger.LogInformation("Order {OrderNumber} focus status changed to {IsFocusOrder} by user {UserId}",
            order.OrderNumber, command.IsFocusOrder, _currentUserService.UserId);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto,
            command.IsFocusOrder ? "Order marked as focus order" : "Order focus status removed");
    }
}
