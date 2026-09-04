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

        // NotNull FIRST, and it is the whole point of making `Role` nullable. `UserRole.Customer`
        // is 0, so before this an omitted `role` key bound to Customer, passed `IsInEnum()`, and
        // DEMOTED the staff member the request was editing — an administrator could be stripped of
        // every permission by a client that simply did not mention the field, and be told the
        // update succeeded.
        //
        // Refusing beats defaulting. "Leave the role unchanged" is a reasonable thing to want, but
        // it cannot be expressed here: this endpoint takes a full representation, and absent and
        // "set it to Customer" are the same bytes on the wire. A 400 naming the field is the only
        // answer that cannot be wrong. The admin UI already sends the field on every save.
        RuleFor(x => x.Role)
            .NotNull().WithMessage("Role is required")
            .IsInEnum().WithMessage("Invalid role specified");
    }
}
