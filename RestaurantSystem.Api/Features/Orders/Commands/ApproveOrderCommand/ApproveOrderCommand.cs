using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using UpdateStatusCommand = RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.ApproveOrderCommand;

public sealed record ApproveOrderCommand : ICommand<ApiResponse<OrderDto>>
{
    public Guid OrderId { get; set; }
    public int PreparationMinutes { get; set; }
    public string? Notes { get; set; }
    public int? ExpectedVersion { get; set; }
}

public sealed class ApproveOrderCommandHandler : ICommandHandler<ApproveOrderCommand, ApiResponse<OrderDto>>
{
    private readonly CustomMediator _mediator;
    private readonly OrderWorkflowSettings _workflow;

    public ApproveOrderCommandHandler(
        CustomMediator mediator,
        IOptions<OrderWorkflowSettings>? workflow = null)
    {
        _mediator = mediator;
        _workflow = workflow?.Value ?? new OrderWorkflowSettings();
    }

    public Task<ApiResponse<OrderDto>> Handle(
        ApproveOrderCommand command,
        CancellationToken cancellationToken)
    {
        var finalStatus = command.PreparationMinutes > _workflow.DelayApprovalThresholdMinutes
            ? OrderStatus.PendingApproval
            : OrderStatus.Confirmed;

        return _mediator.SendCommand(new UpdateStatusCommand
        {
            OrderId = command.OrderId,
            NewStatus = finalStatus,
            EstimatedPreparationMinutes = command.PreparationMinutes,
            Notes = command.Notes,
            ExpectedVersion = command.ExpectedVersion
        }, cancellationToken);
    }
}
