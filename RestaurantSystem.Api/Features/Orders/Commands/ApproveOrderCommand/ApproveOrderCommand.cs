using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
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

    [JsonRequired]
    public int PreparationMinutes { get; set; }
    public string? Notes { get; set; }
    public int? ExpectedVersion { get; set; }
}

public sealed class ApproveOrderCommandHandler : ICommandHandler<ApproveOrderCommand, ApiResponse<OrderDto>>
{
    private readonly CustomMediator _mediator;
    private readonly OrderWorkflowSettings _workflow;
    private readonly ApplicationDbContext _context;

    public ApproveOrderCommandHandler(
        CustomMediator mediator,
        ApplicationDbContext context,
        IOptions<OrderWorkflowSettings>? workflow = null)
    {
        _mediator = mediator;
        _context = context;
        _workflow = workflow?.Value ?? new OrderWorkflowSettings();
    }

    public async Task<ApiResponse<OrderDto>> Handle(
        ApproveOrderCommand command,
        CancellationToken cancellationToken)
    {
        var orderType = await _context.Orders
            .AsNoTracking()
            .Where(order => order.Id == command.OrderId)
            .Select(order => (OrderType?)order.Type)
            .SingleOrDefaultAsync(cancellationToken);

        var confirmationFlow = orderType.HasValue
            ? await _context.OrderTypeConfigurations
                .AsNoTracking()
                .Where(configuration => configuration.OrderType == orderType.Value)
                .Select(configuration => configuration.ConfirmationFlow)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        // In the reviewed hand-off the restaurant is the decision maker. Its chosen preparation
        // time is a promise sent to the guest, never a second approval request sent back to them.
        // The direct flow retains the historical long-delay consent branch for compatibility.
        var preparationMinutes = command.PreparationMinutes == 0
            ? _workflow.ConfirmedPreparationMinutes
            : command.PreparationMinutes;
        var finalStatus = confirmationFlow == OrderConfirmationFlows.Acknowledge
            || command.PreparationMinutes == 0
            || preparationMinutes <= _workflow.DelayApprovalThresholdMinutes
                ? OrderStatus.Confirmed
                : OrderStatus.PendingApproval;

        return await _mediator.SendCommand(new UpdateStatusCommand
        {
            OrderId = command.OrderId,
            NewStatus = finalStatus,
            EstimatedPreparationMinutes = preparationMinutes,
            Notes = command.Notes,
            ExpectedVersion = command.ExpectedVersion
        }, cancellationToken);
    }
}
