using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffRoundCommand;

public sealed class CreateStaffRoundCommandValidator : AbstractValidator<CreateStaffRoundCommand>
{
    public CreateStaffRoundCommandValidator()
    {
        Include(new StaffCounterOrderRequestValidator());
        RuleFor(command => command.ClientOperationId).NotEmpty();
        RuleFor(command => command.Type)
            .Equal(OrderType.DineIn)
            .WithMessage("A staff round must be a dine-in order.");
        RuleFor(command => command.DeliveryAddress)
            .Null()
            .WithMessage("A delivery address is not valid for a dine-in round.");
        RuleFor(command => command.ServiceSessionId)
            .NotEmpty()
            .WithMessage("A dine-in staff round requires an open table service session.");
    }
}
