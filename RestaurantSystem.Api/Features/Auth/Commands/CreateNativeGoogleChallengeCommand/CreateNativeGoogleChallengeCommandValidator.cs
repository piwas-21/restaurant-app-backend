using FluentValidation;
using RestaurantSystem.Api.Features.Auth.NativeSocial;

namespace RestaurantSystem.Api.Features.Auth.Commands.CreateNativeGoogleChallengeCommand;

public sealed class CreateNativeGoogleChallengeCommandValidator : AbstractValidator<CreateNativeGoogleChallengeCommand>
{
    public CreateNativeGoogleChallengeCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
    }
}
