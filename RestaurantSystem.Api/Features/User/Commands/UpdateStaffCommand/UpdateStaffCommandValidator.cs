using FluentValidation;
using RestaurantSystem.Api.Common.Validation;

namespace RestaurantSystem.Api.Features.User.Commands.UpdateStaffCommand;

public class UpdateStaffCommandValidator : AbstractValidator<UpdateStaffCommand>
{
    public UpdateStaffCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(50).WithMessage("First name cannot exceed 50 characters");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(50).WithMessage("Last name cannot exceed 50 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address");

        // The password is OPTIONAL on an update — `UpdateStaffCommand.Password` is `string?` and the
        // handler changes it only `if (!string.IsNullOrWhiteSpace(command.Password))`. This validator
        // required it anyway (a `NotEmpty()` copied in with the strength chain), so an admin editing
        // only a name, email, phone or role was refused by all six password rules at once and the
        // edit could not be saved at all (issue #290). Strength is still enforced on a password that
        // IS supplied — the guard mirrors the handler's condition exactly so the two cannot disagree.
        When(x => !string.IsNullOrWhiteSpace(x.Password), () =>
        {
            RuleFor(x => x.Password).MeetsPasswordPolicy();
        });

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid role specified");
    }
}
