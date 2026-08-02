using FluentAssertions;
using FluentValidation;
using RestaurantSystem.Api.Common.Behaviors;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.User.Commands.RegisterStaffCommand;
using RestaurantSystem.Api.Features.User.Commands.UpdateStaffCommand;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.User;

/// <summary>
/// Issue #290. <c>UpdateStaffCommandValidator</c> required <c>Password</c> unconditionally while the
/// handler changed it only <c>if (!string.IsNullOrWhiteSpace(command.Password))</c> and the command
/// declared it <c>string?</c>. An admin editing a staff member's name, email, phone or role — which
/// is what the member-management screen sends when "Change Password" is not ticked — had the whole
/// update refused, so a staff edit could not be saved at all.
///
/// The refusal was ONE message, "Password is required", not the six it is tempting to assume: the
/// five strength rules all pass on a null value, and the client omits the key rather than sending
/// <c>""</c>. <see cref="NullPasswordProducedExactlyOneError_TheOriginalDefect"/> keeps that
/// measured rather than remembered.
///
/// No host and no database: the defect is entirely in the validator, and these run in milliseconds.
/// </summary>
public class UpdateStaffCommandValidatorTests
{
    private static readonly UpdateStaffCommandValidator Validator = new();

    private static UpdateStaffCommand Command(string? password) => new(
        UserId: Guid.NewGuid(),
        FirstName: "Ada",
        LastName: "Lovelace",
        Email: "ada@example.com",
        PhoneNumber: "+41791234567",
        Password: password,
        Role: UserRole.Server);

    /// <summary>
    /// The whole point of #290: an OMITTED password means "leave it unchanged". `null` is what the
    /// client sends for that — `JSON.stringify` drops an undefined key entirely.
    /// </summary>
    [Fact]
    public void OmittedPassword_IsValid()
    {
        var result = Validator.Validate(Command(null));

        result.IsValid.Should().BeTrue(
            "the handler only touches the password when one is supplied, so an ordinary profile edit " +
            "must not be refused — was: {0}",
            string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    /// <summary>
    /// A blank password is a mistake, not an omission, and is refused rather than ignored.
    ///
    /// This is where the guard deliberately does NOT mirror the handler. The handler skips on
    /// `IsNullOrWhiteSpace`, so a guard copying that condition would let "   " through validation,
    /// skip the update, and still answer "User updated successfully" — the admin types a new
    /// password, is told it was set, and it was not. `is not null` keeps the #290 fix (an omitted
    /// key is untouched) without inheriting that.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPassword_IsRefusedRatherThanSilentlyIgnored(string password)
    {
        var result = Validator.Validate(Command(password));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.ErrorMessage).Should().Contain("Password is required");
    }

    /// <summary>
    /// The original defect, measured rather than described — and the reason the PR text no longer
    /// says "refused by all six password rules". `MinimumLength` and every `Matches` PASS on null,
    /// so the old chain produced exactly one message for the request the client actually sends.
    /// </summary>
    [Fact]
    public void NullPasswordProducedExactlyOneError_TheOriginalDefect()
    {
        var old = new InlineValidator<UpdateStaffCommand>();
        old.RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MeetsPasswordPolicy();

        var errors = old.Validate(Command(null)).Errors.Select(e => e.ErrorMessage).ToList();

        errors.Should().ContainSingle().Which.Should().Be("Password is required");
    }

    [Theory]
    [InlineData("Sh0rt!", "Password must be at least 8 characters long")]
    [InlineData("nouppercase1!", "Password must contain at least one uppercase letter")]
    [InlineData("NOLOWERCASE1!", "Password must contain at least one lowercase letter")]
    [InlineData("NoDigitsHere!", "Password must contain at least one digit")]
    [InlineData("NoSpecials123", "Password must contain at least one special character")]
    public void WeakPasswordSupplied_IsStillRefused(string password, string expectedMessage)
    {
        var result = Validator.Validate(Command(password));

        result.IsValid.Should().BeFalse("making the password optional must not make it unchecked");
        result.Errors.Select(e => e.ErrorMessage).Should().Contain(expectedMessage);
    }

    [Fact]
    public void StrongPasswordSupplied_IsValid()
    {
        Validator.Validate(Command("ValidPass123!")).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// The non-password rules must be untouched by the `When(...)` block — a guard written around
    /// the whole validator rather than around the password rules would disable these too, and an
    /// empty name would then reach the handler.
    /// </summary>
    [Fact]
    public void OtherRulesStillApplyWhenNoPasswordIsSupplied()
    {
        var command = new UpdateStaffCommand(
            UserId: Guid.NewGuid(),
            FirstName: "",
            LastName: "",
            Email: "not-an-email",
            PhoneNumber: null,
            Password: null,
            Role: UserRole.Server);

        var messages = Validator.Validate(command).Errors.Select(e => e.ErrorMessage).ToList();

        messages.Should().Contain("First name is required");
        messages.Should().Contain("Last name is required");
        messages.Should().Contain("Email must be a valid email address");
    }
}

/// <summary>
/// Registration is the OTHER side of the same extraction: `PasswordRules.MeetsPasswordPolicy` is now
/// shared, so a change there could silently make a password optional at signup too. It must not.
///
/// The message strings are asserted verbatim rather than by rule count because the frontend routes
/// them onto form fields by matching their text (`apiFormErrors.ts`), and mirrors them in
/// `lib/passwordPolicy.ts`. A reworded message is a silent UI regression, not a refactor.
/// </summary>
public class RegisterStaffPasswordRulesTests
{
    private static readonly RegisterStaffCommandValidator Validator = new();

    private static RegisterStaffCommand Command(string? password) => new(
        FirstName: "Ada",
        LastName: "Lovelace",
        Email: "ada@example.com",
        Password: password!,
        ConfirmPassword: password!,
        Role: UserRole.Server);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PasswordIsStillRequiredAtRegistration(string? password)
    {
        var result = Validator.Validate(Command(password));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.ErrorMessage).Should().Contain("Password is required");
    }

    [Fact]
    public void TheSharedStrengthMessagesAreUnchanged()
    {
        var messages = Validator.Validate(Command("weak")).Errors.Select(e => e.ErrorMessage).ToList();

        messages.Should().Contain("Password must be at least 8 characters long");
        messages.Should().Contain("Password must contain at least one uppercase letter");
        messages.Should().Contain("Password must contain at least one digit");
        messages.Should().Contain("Password must contain at least one special character");
    }
}

/// <summary>
/// What a validation failure ACTUALLY looks like by the time it leaves the API — pinned because the
/// documented version was wrong, in this repo and in the frontend that consumes it.
///
/// The story everywhere was: FluentValidation failures become a 400 whose <c>errors[]</c> carries
/// one entry per broken rule, produced by <c>ValidationExceptionHandlingMiddleware</c>. Neither half
/// holds. <see cref="ValidationBehavior{TRequest,TResponse}"/> joins every message with "; " into a
/// single <see cref="BadRequestException"/>, which <c>ExceptionHandlingMiddleware</c> maps to a 400
/// whose <c>errors[]</c> has exactly ONE element. Nothing in the solution throws FluentValidation's
/// <c>ValidationException</c> at all, so the middleware named after it never runs (issue #291).
///
/// The consequence is not cosmetic: the frontend routes per-rule messages onto individual form
/// fields by matching each entry, and with one blob it matches the first field and files the whole
/// string there. Pinning it here so the next person reads the behaviour instead of the legend.
///
/// Scope, stated because an earlier version of this comment overreached: these run host-free, so
/// they pin the two ends that are reachable without one — what `ValidationBehavior` throws, and
/// what `ApiResponse.Failure` makes of it. The middleware hop between them
/// (`ExceptionHandlingMiddleware.cs:74-78,104-107`) is read, not executed, and in Development it
/// substitutes `exception.ToString()` for the message.
/// </summary>
public class UpdateStaffValidationContractTests
{
    [Fact]
    public async Task EveryBrokenRule_ArrivesAsOneSemicolonJoinedMessage()
    {
        var behavior = new ValidationBehavior<UpdateStaffCommand, ApiResponse<AuthResponse>>(
            new IValidator<UpdateStaffCommand>[] { new UpdateStaffCommandValidator() });

        var command = new UpdateStaffCommand(
            UserId: Guid.NewGuid(),
            FirstName: "Ada",
            LastName: "Lovelace",
            Email: "ada@example.com",
            PhoneNumber: null,
            Password: "weak",
            Role: UserRole.Server);

        var act = async () => await behavior.Handle(
            command,
            () => Task.FromResult(ApiResponse<AuthResponse>.SuccessWithData(new AuthResponse(), "unreachable")),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<BadRequestException>();

        // One message, not a list — the "; " is the backend's own join, not the client's.
        thrown.Which.Message.Should().Contain("; ");
        thrown.Which.Message.Should().Contain("Password must be at least 8 characters long");
        thrown.Which.Message.Should().Contain("Password must contain at least one uppercase letter");

        // …and the other end of the mapping: that single message becomes a single-element errors[],
        // which is the fact the frontend's field routing actually depends on.
        var body = ApiResponse<object>.Failure(thrown.Which.Message, thrown.Which.Message);
        body.Errors.Should().ContainSingle("per-rule entries never survive to the client — issue #291");
        body.Errors![0].Should().Contain("; ");
    }

    [Fact]
    public async Task NoPassword_ReachesTheHandler()
    {
        var behavior = new ValidationBehavior<UpdateStaffCommand, ApiResponse<AuthResponse>>(
            new IValidator<UpdateStaffCommand>[] { new UpdateStaffCommandValidator() });

        var command = new UpdateStaffCommand(
            UserId: Guid.NewGuid(),
            FirstName: "Ada",
            LastName: "Lovelace",
            Email: "ada@example.com",
            PhoneNumber: null,
            Password: null,
            Role: UserRole.Server);

        var handlerRan = false;

        await behavior.Handle(
            command,
            () =>
            {
                handlerRan = true;
                return Task.FromResult(ApiResponse<AuthResponse>.SuccessWithData(new AuthResponse(), "ok"));
            },
            CancellationToken.None);

        handlerRan.Should().BeTrue("issue #290: the pipeline refused the command before the handler saw it");
    }
}
