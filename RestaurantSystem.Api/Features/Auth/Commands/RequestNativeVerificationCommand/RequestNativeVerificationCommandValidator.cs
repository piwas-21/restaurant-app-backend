using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.RequestNativeVerificationCommand;

public sealed class RequestNativeVerificationCommandValidator : AbstractValidator<RequestNativeVerificationCommand>
{
    public RequestNativeVerificationCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.Email).NotEmpty().MaximumLength(256).EmailAddress();
    }
}
