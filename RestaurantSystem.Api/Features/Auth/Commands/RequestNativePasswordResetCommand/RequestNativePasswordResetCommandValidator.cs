using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.RequestNativePasswordResetCommand;

public sealed class RequestNativePasswordResetCommandValidator : AbstractValidator<RequestNativePasswordResetCommand>
{
    public RequestNativePasswordResetCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.Email).NotEmpty().MaximumLength(256).EmailAddress();
    }
}
