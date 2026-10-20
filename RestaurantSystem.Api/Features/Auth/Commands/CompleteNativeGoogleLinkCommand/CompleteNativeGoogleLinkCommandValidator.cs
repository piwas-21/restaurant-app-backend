using FluentValidation;
using RestaurantSystem.Api.Features.Auth.NativeSocial;

namespace RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeGoogleLinkCommand;

public sealed class CompleteNativeGoogleLinkCommandValidator : AbstractValidator<CompleteNativeGoogleLinkCommand>
{
    public CompleteNativeGoogleLinkCommandValidator()
    {
        RuleFor(command => command.SchemaVersion).Equal(1);
        RuleFor(command => command.ChallengeId).NotEmpty();
        RuleFor(command => command.Proof).Must(NativeSocialChallengeProof.IsEncodedProof);
        RuleFor(command => command.IdToken).NotEmpty().MaximumLength(NativeGoogleIdentityVerifier.MaximumTokenLength);
    }
}
