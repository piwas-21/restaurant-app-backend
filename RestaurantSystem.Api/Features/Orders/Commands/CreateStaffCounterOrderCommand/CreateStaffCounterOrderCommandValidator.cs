using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Orders.Commands.StaffCounterOrderValidation;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateStaffCounterOrderCommand;

public sealed class CreateStaffCounterOrderCommandValidator : AbstractValidator<CreateStaffCounterOrderCommand>
{
    public CreateStaffCounterOrderCommandValidator(IOptions<FidelitySettings> fidelitySettings)
    {
        Include(new StaffCounterOrderRequestValidator(fidelitySettings));
        RuleFor(command => command.ClientOperationId).NotEmpty();
    }
}
