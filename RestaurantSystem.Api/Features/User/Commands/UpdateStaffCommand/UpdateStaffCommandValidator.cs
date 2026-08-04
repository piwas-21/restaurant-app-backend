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
        // only a name, email, phone or role had the whole update refused (issue #290).
        //
        // The guard is `is not null`, NOT the handler's `IsNullOrWhiteSpace`. Mirroring the handler
        // exactly would mean a password of "   " — something the admin explicitly typed — silently
        // skips both the rules and the update, and the response still says the user was updated. A
        // key the client OMITS arrives as null and is genuinely "leave it unchanged"; a blank string
        // is a mistake, and it gets told so.
        When(x => x.Password is not null, () =>
        {
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MeetsPasswordPolicy();
        });

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid role specified");
    }
}
