using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.Auth.Commands.SetPasswordCommand;

/// <summary>
/// Same strength rules as register, reset-password and change-password —
/// <see cref="PasswordRules.MeetsPasswordPolicy{T}"/>, never a sixth copy of them (#290, #292).
/// Requiredness stays here, where the field names live.
/// </summary>
public class SetPasswordCommandValidator : AbstractValidator<SetPasswordCommand>
{
    public SetPasswordCommandValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MeetsPasswordPolicy();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Password confirmation is required")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}
