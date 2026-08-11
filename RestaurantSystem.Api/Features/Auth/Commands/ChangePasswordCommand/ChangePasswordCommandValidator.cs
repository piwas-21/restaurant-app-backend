using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.Auth.Commands.ChangePasswordCommand;

/// <summary>
/// The last validator that carried its own copy of the password strength rules (#292).
///
/// It had drifted from the other four in two ways, and the second was a real behavioural
/// divergence rather than wording: a <c>MaximumLength(100)</c> that NOTHING else in the system
/// enforced — not signup, not reset, not staff registration, not Identity's own options — so a
/// password accepted when the account was created was refused when the user tried to change to it,
/// with no way to discover the ceiling beforehand (<c>lib/passwordPolicy.ts</c> mirrors no maximum
/// either). The ceiling is dropped rather than propagated: it protected nothing that the request
/// size limit does not already bound, and adding it to <see cref="PasswordRules"/> would have newly
/// refused passwords every other path accepts today.
///
/// The wording ("New password must…") is gone with it. The frontend routes these onto form fields
/// with a broad <c>/password/i</c>, so field routing is unaffected — but two vocabularies meant
/// <c>lib/passwordPolicy.ts</c>'s client-side mirror could only ever match one of them.
///
/// The requiredness messages stay first-person to this command: "Current password" and "New
/// password" are what distinguish the two fields, and <see cref="PasswordRules"/> deliberately
/// says nothing about whether a value must be present (#290).
/// </summary>
public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MeetsPasswordPolicy();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Password confirmation is required")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}
