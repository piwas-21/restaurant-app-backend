using FluentValidation;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Features.OrderAmendments.Commands;

public sealed record CommitOrderAmendmentCommand(Guid OrderId, OrderAmendmentCommitRequest Request)
    : ICommand<ApiResponse<OrderAmendmentCommitDto>>;

public sealed class CommitOrderAmendmentCommandValidator : AbstractValidator<CommitOrderAmendmentCommand>
{
    public CommitOrderAmendmentCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Request).NotNull();
        RuleFor(command => command.Request.AmendmentId).NotEmpty();
        RuleFor(command => command.Request.ClientOperationId).NotEmpty();
        RuleFor(command => command.Request.ExpectedOrderVersion).GreaterThan(0);
        RuleFor(command => command.Request.ExpectedAccountRevision)
            .GreaterThan(0)
            .When(command => command.Request.ExpectedAccountRevision.HasValue);
        RuleFor(command => command.Request.ReviewAcknowledged).Equal(true)
            .WithMessage("Review the amendment quote before committing it.");
    }
}

public sealed class CommitOrderAmendmentCommandHandler
    : ICommandHandler<CommitOrderAmendmentCommand, ApiResponse<OrderAmendmentCommitDto>>
{
    private readonly IOrderAmendmentCommitService _commits;

    public CommitOrderAmendmentCommandHandler(IOrderAmendmentCommitService commits) => _commits = commits;

    public async Task<ApiResponse<OrderAmendmentCommitDto>> Handle(
        CommitOrderAmendmentCommand command, CancellationToken cancellationToken)
    {
        var result = await _commits.CommitAsync(command.OrderId, command.Request, cancellationToken);
        return ApiResponse<OrderAmendmentCommitDto>.SuccessWithData(result, "Order amendment committed");
    }
}
