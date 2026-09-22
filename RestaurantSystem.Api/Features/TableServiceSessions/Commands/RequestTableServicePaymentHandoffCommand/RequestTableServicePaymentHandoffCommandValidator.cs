using FluentValidation;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RequestTableServicePaymentHandoffCommand;

public sealed class RequestTableServicePaymentHandoffCommandValidator
    : AbstractValidator<RequestTableServicePaymentHandoffCommand>
{
    public RequestTableServicePaymentHandoffCommandValidator()
    {
        RuleFor(command => command.OperationId)
            .NotEmpty()
            .WithMessage("Operation id is required");
        RuleFor(command => command.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("Expected session version is required");
    }
}
