using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

// FluentValidation IS wired (CustomMediator resolves IValidator<TCommand>
// and calls ValidateAndThrowAsync on every dispatch — see Common/CustomMediator.cs).
// The previous rule set was deliberately commented out and is preserved in
// git history. Repopulate this body when ready; rules will fire automatically.
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
}
