using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using UpdateStatusCommand = RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand.UpdateOrderStatusCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.ApproveOrderCommand;

public sealed record ApproveOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    [JsonIgnore]
    public Guid OrderId { get; set; }

    public int? PreparationMinutes { get; set; }
    public string? Notes { get; set; }
    public int? ExpectedVersion { get; set; }
}

public sealed class ApproveOrderCommandHandler : ICommandHandler<ApproveOrderCommand, ApiResponse<OrderDto>>
{
    private readonly CustomMediator _mediator;
    private readonly OrderWorkflowSettings _workflow;
    private readonly ApplicationDbContext _context;
    private readonly IOrderPreparationService _preparation;

    public ApproveOrderCommandHandler(
        CustomMediator mediator,
        ApplicationDbContext context,
        IOrderPreparationService preparation,
        IOptions<OrderWorkflowSettings>? workflow = null)
    {
        _mediator = mediator;
        _context = context;
        _preparation = preparation;
        _workflow = workflow?.Value ?? new OrderWorkflowSettings();
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        ApproveOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.OrderId && !candidate.IsDeleted,
                cancellationToken);
        if (order is null)
            return ApiResponse<OrderDto>.Failure("Order not found");

        var confirmationFlow = await _context.OrderTypeConfigurations
                .AsNoTracking()
                .Where(configuration => configuration.OrderType == order.Type)
                .Select(configuration => configuration.ConfirmationFlow)
                .SingleOrDefaultAsync(cancellationToken);

        // In the reviewed hand-off the restaurant is the decision maker. Its chosen preparation
        // time is a promise sent to the guest, never a second approval request sent back to them.
        // The direct flow retains the historical long-delay consent branch for compatibility.
        var preparationMinutes = await _preparation.ResolveEffectiveMinutesAsync(
            order, command.PreparationMinutes, cancellationToken);
        var finalStatus = confirmationFlow == OrderConfirmationFlows.Acknowledge
            || preparationMinutes <= _workflow.DelayApprovalThresholdMinutes
                ? OrderStatus.Confirmed
                : OrderStatus.PendingApproval;

        return await _mediator.SendCommand(new UpdateStatusCommand
        {
            OrderId = command.OrderId,
            NewStatus = finalStatus,
            EstimatedPreparationMinutes = command.PreparationMinutes,
            Notes = command.Notes,
            ExpectedVersion = command.ExpectedVersion
        }, cancellationToken);
    }
}
