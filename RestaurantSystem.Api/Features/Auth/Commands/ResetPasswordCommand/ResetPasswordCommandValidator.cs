using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.Auth.Commands.ResetPasswordCommand;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Reset token is required");

        // The strength messages here were already byte-identical to the shared ones, so this is a
        // pure extraction. Only the "required" message differs, and it stays at the callsite where
        // requiredness belongs. `ChangePasswordCommandValidator` is the one copy NOT swept up — its
        // wording has already drifted and it carries a MaximumLength(100) nothing else enforces, so
        // unifying it changes user-facing text (issue #292).
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MeetsPasswordPolicy();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}
