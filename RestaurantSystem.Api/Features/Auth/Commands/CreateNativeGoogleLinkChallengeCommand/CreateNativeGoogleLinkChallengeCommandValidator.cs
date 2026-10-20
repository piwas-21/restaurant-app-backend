using FluentValidation;
using RestaurantSystem.Api.Features.Auth.NativeSocial;

namespace RestaurantSystem.Api.Features.Auth.Commands.CreateNativeGoogleLinkChallengeCommand;

public sealed class CreateNativeGoogleLinkChallengeCommandValidator : AbstractValidator<CreateNativeGoogleLinkChallengeCommand>
{
    public CreateNativeGoogleLinkChallengeCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(1000);
    }
}
