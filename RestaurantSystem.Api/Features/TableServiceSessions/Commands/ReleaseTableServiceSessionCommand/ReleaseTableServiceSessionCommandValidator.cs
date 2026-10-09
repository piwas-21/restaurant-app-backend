using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.ReleaseTableServiceSessionCommand;

public sealed class ReleaseTableServiceSessionCommandValidator : AbstractValidator<ReleaseTableServiceSessionCommand>
{
    public ReleaseTableServiceSessionCommandValidator() =>
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
}
