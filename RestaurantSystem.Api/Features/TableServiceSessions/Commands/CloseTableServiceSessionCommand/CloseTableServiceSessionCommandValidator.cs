using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;

public sealed class CloseTableServiceSessionCommandValidator
    : AbstractValidator<CloseTableServiceSessionCommand>
{
    public CloseTableServiceSessionCommandValidator()
    {
        RuleFor(command => command.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("Expected session version is required");
    }
}
