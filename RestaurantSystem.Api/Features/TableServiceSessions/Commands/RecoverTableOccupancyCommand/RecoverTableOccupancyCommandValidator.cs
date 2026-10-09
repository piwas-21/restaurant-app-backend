using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;

public sealed class RecoverTableOccupancyCommandValidator : AbstractValidator<RecoverTableOccupancyCommand>
{
    public RecoverTableOccupancyCommandValidator()
    {
        RuleFor(command => command.TableId).NotEmpty();
        RuleFor(command => command.OperationId).NotEmpty();
        RuleFor(command => command.ExpectedReadinessVersion).GreaterThan(0);
        RuleFor(command => command.PreviewFingerprint)
            .Length(64).Matches("^[A-Fa-f0-9]{64}$");
        RuleFor(command => command.ConfirmRecovery).Equal(true);
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
        RuleFor(command => command.ServiceSessionId)
            .Must(value => !value.HasValue || value != Guid.Empty);
        RuleFor(command => command.ExpectedSessionVersion).GreaterThan(0)
            .When(command => command.ServiceSessionId.HasValue);
        RuleFor(command => command.ExpectedAccountRevision).GreaterThan(0)
            .When(command => command.ServiceSessionId.HasValue);
        RuleFor(command => command.ExpectedSessionVersion).Null()
            .When(command => !command.ServiceSessionId.HasValue);
        RuleFor(command => command.ExpectedAccountRevision).Null()
            .When(command => !command.ServiceSessionId.HasValue);
    }
}
