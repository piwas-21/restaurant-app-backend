using FluentValidation;
using RestaurantSystem.Api.Features.Auth.NativeSocial;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeGoogleLoginCommand;

public sealed class CompleteNativeGoogleLoginCommandValidator : AbstractValidator<CompleteNativeGoogleLoginCommand>
{
    public CompleteNativeGoogleLoginCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.ChallengeId).NotEmpty();
        RuleFor(command => command.Proof).Must(NativeSocialChallengeProof.IsEncodedProof);
        RuleFor(command => command.IdToken).NotEmpty().MaximumLength(NativeGoogleIdentityVerifier.MaximumTokenLength);
        RuleFor(command => command.FirstName).MaximumLength(50);
        RuleFor(command => command.LastName).MaximumLength(50);
    }
}
