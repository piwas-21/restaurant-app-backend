using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.ApproveOrderCommand;

public sealed class ApproveOrderCommandValidator : AbstractValidator<ApproveOrderCommand>
{
    public ApproveOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.PreparationMinutes).InclusiveBetween(0, 600);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0).When(command => command.ExpectedVersion.HasValue);
    }
}
