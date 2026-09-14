using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;

public sealed class AddTableServiceSessionPaymentCommandValidator
    : AbstractValidator<AddTableServiceSessionPaymentCommand>
{
    public AddTableServiceSessionPaymentCommandValidator()
    {
        RuleFor(command => command.OperationId)
            .NotEmpty()
            .WithMessage("Operation id is required");
        RuleFor(command => command.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("Expected session version is required");
        RuleFor(command => command.PaymentMethod)
            .IsInEnum()
            .WithMessage("Invalid payment method");
        RuleFor(command => command.Amount)
            .GreaterThan(0)
            .WithMessage("Payment amount must be greater than 0");
        RuleFor(command => command.Currency)
            .Matches("^[a-zA-Z]{3}$")
            .When(command => command.Currency != null)
            .WithMessage("Currency must be a 3-letter ISO-4217 code.");
    }
}
