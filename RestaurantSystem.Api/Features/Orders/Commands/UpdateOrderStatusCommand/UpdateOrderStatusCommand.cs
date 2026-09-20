using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public record UpdateOrderStatusCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string? Notes { get; set; }

    /// <summary>Optional detail version; old clients may omit it.</summary>
    public int? ExpectedVersion { get; set; }
    public int? EstimatedPreparationMinutes { get; set; }
}

public partial class UpdateOrderStatusCommandHandler : ICommandHandler<UpdateOrderStatusCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderEventService _orderEventService;
    private readonly ILogger<UpdateOrderStatusCommandHandler> _logger;
    private readonly IOrderResponseProjector _responses;
    private readonly IOrderNotificationService _notifications;
    private readonly OrderWorkflowSettings _workflow;

    public UpdateOrderStatusCommandHandler(
          ApplicationDbContext context,
          ICurrentUserService currentUserService,
          IOrderEventService orderEventService,
          IOrderResponseProjector responses,
          IOrderNotificationService notifications,
          ILogger<UpdateOrderStatusCommandHandler> logger,
          IOptions<OrderWorkflowSettings>? workflow = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderEventService = orderEventService;
        _responses = responses;
        _notifications = notifications;
        _logger = logger;
        _workflow = workflow?.Value ?? new OrderWorkflowSettings();
    }

    public async Task<ApiResponse<OrderDto>> Handle(UpdateOrderStatusCommand command, CancellationToken cancellationToken)
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

        NormalizePreparationApprovalStatus(command);
        var validationFailure = ValidateOrderStatusUpdate(order, command);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var previousStatus = order.Status.ToString();
        var statusHistory = CreateStatusHistory(order, command);

        _context.OrderStatusHistories.Add(statusHistory);

        order.Status = command.NewStatus;
        order.UpdatedAt = DateTime.UtcNow;
        order.UpdatedBy = _currentUserService.GetAuditIdentifier();

        var notificationPreparationMinutes = ApplyStatusChange(order, command);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The order changed. Refresh it before updating its status.",
                ErrorCodes.OrderVersionConflict);
        }

        await SendStatusNotificationAsync(
            order, command.NewStatus, notificationPreparationMinutes, cancellationToken);

        var orderDto = await _responses.ProjectAsync(order, cancellationToken);

        await NotifyStatusEventsAsync(orderDto, previousStatus, command.NewStatus);

        _logger.LogInformation("Order {OrderNumber} status updated from {FromStatus} to {ToStatus} by user {UserId}",
            order.OrderNumber, statusHistory.FromStatus, statusHistory.ToStatus, _currentUserService.UserId);

        return ApiResponse<OrderDto>.SuccessWithData(orderDto, "Order status updated successfully");
    }
}
