using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;

public sealed class OpenTableServiceSessionCommandValidator : AbstractValidator<OpenTableServiceSessionCommand>
{
    public OpenTableServiceSessionCommandValidator()
    {
        RuleFor(command => command.TableNumber)
            .GreaterThan(0)
            .When(command => command.TableNumber.HasValue)
            .WithMessage("Table number must be greater than 0");
        RuleFor(command => command)
            .Must(command => command.TableId.HasValue || command.TableNumber.HasValue)
            .WithMessage("A stable table id or table number is required.");
        RuleFor(command => command.Currency)
            .Matches("^[a-zA-Z]{3}$")
            .When(command => command.Currency != null)
            .WithMessage("Currency must be a 3-letter ISO-4217 code.");
    }
}
