using FluentValidation;

namespace RestaurantSystem.Api.Features.Payments.Commands.CreateCheckoutSessionCommand;

public class CreateCheckoutSessionCommandValidator : AbstractValidator<CreateCheckoutSessionCommand>
{
    public CreateCheckoutSessionCommandValidator()
    {
        // The only field there is. An empty Guid would otherwise reach the database as a lookup
        // that cannot match, and answer 404 "Order not found" — technically true, but it reads as
        // "your order is gone" rather than "you sent nothing".
        RuleFor(c => c.OrderId).NotEmpty().WithMessage("An order id is required.");
    }
}
