using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Commands.DeliverServerTaskCommand;

public sealed record DeliverServerTaskCommand(
    Guid OrderId,
    int ExpectedVersion) : ICommand<ApiResponse<OrderDto>>;

public sealed class DeliverServerTaskCommandHandler
    : ICommandHandler<DeliverServerTaskCommand, ApiResponse<OrderDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderPermittedActionsService _actions;
    private readonly CustomMediator _mediator;

    public DeliverServerTaskCommandHandler(
        ApplicationDbContext context,
        IOrderPermittedActionsService actions,
        CustomMediator mediator)
    {
        _context = context;
        _actions = actions;
        _mediator = mediator;
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        DeliverServerTaskCommand command,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(value => value.RoutingStates)
            .FirstOrDefaultAsync(value => value.Id == command.OrderId && !value.IsDeleted,
                cancellationToken);
        if (order is null)
        {
            return ApiResponse<OrderDto>.Failure("Order not found.");
        }

        if (order.Version != command.ExpectedVersion)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "The order changed. Refresh it before delivering it.",
                ErrorCodes.OrderVersionConflict);
        }

        if (ServerTaskRoutingPolicy.HasRequiredException(order))
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "Resolve required order routing before delivering it.",
                ErrorCodes.RequiredRoutingUnresolved);
        }

        var handOver = _actions.GetPermittedActions(order)
            .Single(action => action.Action == nameof(OrderAction.HandOver));
        if (!handOver.Allowed)
        {
            return ApiResponse<OrderDto>.FailureWithCode(
                "This order cannot be delivered by the current staff member.",
                handOver.ReasonCode ?? OrderActionReasonCodes.InvalidStatusTransition);
        }

        return await _mediator.SendCommand(new UpdateOrderStatusCommand
        {
            OrderId = command.OrderId,
            ExpectedVersion = command.ExpectedVersion,
            NewStatus = ResolveTargetStatus(order),
        }, cancellationToken);
    }

    private static OrderStatus ResolveTargetStatus(Order order) =>
        order.Status == OrderStatus.Ready && order.Type == OrderType.Delivery
            ? OrderStatus.OutForDelivery
            : OrderStatus.Completed;
}
