using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeVerificationCommand;

public sealed class CompleteNativeVerificationCommandValidator : AbstractValidator<CompleteNativeVerificationCommand>
{
    public CompleteNativeVerificationCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.Proof).Must(RestaurantSystem.Api.Features.Auth.Services.CustomerNativeAccountLinks.ValidProof);
    }
}
