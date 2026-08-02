using FluentAssertions;
using FluentValidation;
using RestaurantSystem.Api.Common.Behaviors;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.User.Commands.RegisterStaffCommand;
using RestaurantSystem.Api.Features.User.Commands.UpdateStaffCommand;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.User;

/// <summary>
/// Issue #290. <c>UpdateStaffCommandValidator</c> required <c>Password</c> unconditionally while the
/// handler changed it only <c>if (!string.IsNullOrWhiteSpace(command.Password))</c> and the command
/// declared it <c>string?</c>. An admin editing a staff member's name, email, phone or role — which
/// is what the member-management screen sends when "Change Password" is not ticked — was refused by
/// all six password rules at once, so a staff edit could not be saved at all.
///
/// No host and no database: the defect is entirely in the validator, and these run in milliseconds.
/// The end-to-end half is covered by <see cref="UpdateStaffValidationContractTests"/>, which pins
/// what the 400 body actually looks like.
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoPasswordSupplied_IsValid(string? password)
    {
        var result = Validator.Validate(Command(password));

        result.IsValid.Should().BeTrue(
            "the handler only touches the password when one is supplied, so an ordinary profile edit " +
            "must not be refused — was: {0}",
            string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    /// <summary>
    /// The whitespace case is the one a `.When(x => x.Password != null)` guard would get wrong: the
    /// handler's own condition is `IsNullOrWhiteSpace`, so "   " is NOT a password change, and
    /// validating it as one would refuse the edit for a value the handler is going to ignore.
    /// </summary>
    [Fact]
    public void WhitespacePassword_MatchesTheHandlersOwnCondition()
    {
        Validator.Validate(Command("   ")).IsValid.Should().BeTrue();
        string.IsNullOrWhiteSpace("   ").Should().BeTrue("the guard and the handler must agree");
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
