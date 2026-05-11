using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    // Rules intentionally not yet defined — validator placeholder for future FluentValidation
    // pipeline (see backend issue #9). The compiler-generated parameterless ctor is sufficient.
}
