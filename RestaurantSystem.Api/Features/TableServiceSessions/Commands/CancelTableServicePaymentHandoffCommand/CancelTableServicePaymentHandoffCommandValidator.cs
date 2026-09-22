using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CancelTableServicePaymentHandoffCommand;

public sealed class CancelTableServicePaymentHandoffCommandValidator
    : AbstractValidator<CancelTableServicePaymentHandoffCommand>
{
    public CancelTableServicePaymentHandoffCommandValidator()
    {
        RuleFor(command => command.OperationId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
    }
}
