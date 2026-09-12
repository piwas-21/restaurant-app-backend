using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;

public sealed class OpenTableServiceSessionCommandValidator : AbstractValidator<OpenTableServiceSessionCommand>
{
    public OpenTableServiceSessionCommandValidator()
    {
        RuleFor(command => command.TableNumber)
            .GreaterThan(0)
            .WithMessage("Table number must be greater than 0");
        RuleFor(command => command.Currency)
            .Matches("^[a-zA-Z]{3}$")
            .When(command => command.Currency != null)
            .WithMessage("Currency must be a 3-letter ISO-4217 code.");
    }
}
