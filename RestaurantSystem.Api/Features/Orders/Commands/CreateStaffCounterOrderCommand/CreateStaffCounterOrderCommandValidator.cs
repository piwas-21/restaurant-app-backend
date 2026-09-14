using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;

public sealed class CreateStaffCounterOrderCommandValidator : AbstractValidator<CreateStaffCounterOrderCommand>
{
    public CreateStaffCounterOrderCommandValidator()
    {
        Include(new StaffCounterOrderRequestValidator());
        RuleFor(command => command.ClientOperationId).NotEmpty();
    }
}
