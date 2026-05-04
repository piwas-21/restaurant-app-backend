using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.VerifyEmailCommand;

public class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Verification token is required");
    }
}
