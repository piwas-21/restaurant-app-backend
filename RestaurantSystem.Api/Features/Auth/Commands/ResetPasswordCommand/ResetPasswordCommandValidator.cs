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
        // requiredness belongs. `ChangePasswordCommandValidator` was the one copy NOT swept up by
        // #290; #292 has since brought it in and dropped the MaximumLength(100) it alone carried,
        // so all five paths now share exactly these rules.
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MeetsPasswordPolicy();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}
