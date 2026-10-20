using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativePasswordResetCommand;

public sealed class CompleteNativePasswordResetCommandValidator : AbstractValidator<CompleteNativePasswordResetCommand>
{
    public CompleteNativePasswordResetCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.Proof).Must(RestaurantSystem.Api.Features.Auth.Services.CustomerNativeAccountLinks.ValidProof);
        RuleFor(command => command.NewPassword).NotEmpty().MaximumLength(1000);
        RuleFor(command => command.ConfirmPassword).NotEmpty().MaximumLength(1000);
    }
}
