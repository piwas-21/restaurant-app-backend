using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;

public sealed class MarkTableReadyCommandValidator : AbstractValidator<MarkTableReadyCommand>
{
    public MarkTableReadyCommandValidator()
    {
        RuleFor(command => command.TableId).NotEmpty();
        RuleFor(command => command.OperationId).NotEmpty();
        RuleFor(command => command.ExpectedReadinessVersion).GreaterThan(0);
    }
}
