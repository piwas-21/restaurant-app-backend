using FluentAssertions;
using FluentValidation;
using RestaurantSystem.Api.Features.Auth.Commands.ChangePasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.ResetPasswordCommand;
using RestaurantSystem.Api.Features.User.Commands.RegisterCustomerCommand;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// Every password path enforces the SAME strength rules (#292).
///
/// #290 extracted the rules into <c>PasswordRules.MeetsPasswordPolicy()</c> and pointed four
/// validators at it. <c>ChangePasswordCommandValidator</c> was deliberately left out because it had
/// already drifted, and the drift was not only wording: it carried a <c>MaximumLength(100)</c> that
/// NOTHING else enforced — not signup, not reset, not staff registration, and not Identity's own
/// options, which set `RequiredLength` and no ceiling at all.
///
/// So a password could be accepted when the account was created and refused when the user tried to
/// change to it, with no way to find out first: `lib/passwordPolicy.ts` mirrors the five strength
/// rules client-side and no maximum, so the frontend could not warn either. That asymmetry is the
/// bug, and it is what the first test here pins.
///
/// These run host-free on purpose: the question is what the RULES are, not what a request does with
/// them, and a validator is the smallest thing that can answer it.
///
/// Scope, stated rather than implied: three of the five <c>MeetsPasswordPolicy</c> callsites are
/// exercised here — signup, reset and change. <c>RegisterStaffCommandValidator</c> and
/// <c>UpdateStaffCommandValidator</c> are covered by <c>RegisterStaffPasswordRulesTests</c> in
/// <c>UpdateStaffCommandValidatorTests</c>, so they are not a hole, but nothing in THIS file
/// touches them.
/// </summary>
public class PasswordPolicyParityTests
{
    /// <summary>101 characters, and strong: only a length ceiling can refuse this.</summary>
    private const string LongButStrongPassword =
        "Str0ng!Passw0rd-that-is-deliberately-longer-than-one-hundred-characters-to-trip-any-ceiling-abcdefghij";

    private static List<string> Failures<T>(AbstractValidator<T> validator, T instance) =>
        validator.Validate(instance).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void LongPassword_AcceptedAtSignupAndReset_IsNowAlsoAcceptedOnChange()
    {
        LongButStrongPassword.Length.Should().BeGreaterThan(100, "otherwise this test cannot see a 100-char ceiling");

        var signup = Failures(
            new RegisterCustomerCommandValidator(),
            new RegisterCustomerCommand("Ada", "Lovelace", "ada@example.com", LongButStrongPassword, LongButStrongPassword));

        var reset = Failures(
            new ResetPasswordCommandValidator(),
            new ResetPasswordCommand("ada@example.com", "token", LongButStrongPassword, LongButStrongPassword));

        var change = Failures(
            new ChangePasswordCommandValidator(),
            new ChangePasswordCommand("Old!Passw0rd", LongButStrongPassword, LongButStrongPassword));

        // The asymmetry #292 reported: the first two were empty and the third was not.
        signup.Should().BeEmpty();
        reset.Should().BeEmpty();
        change.Should().BeEmpty("a password accepted at signup and reset must not be refused on change (#292)");
    }

    [Fact]
    public void AllThreePaths_RefuseAWeakPasswordWithIdenticalWording()
    {
        const string weak = "weak";

        var signup = Failures(
            new RegisterCustomerCommandValidator(),
            new RegisterCustomerCommand("Ada", "Lovelace", "ada@example.com", weak, weak));

        var reset = Failures(
            new ResetPasswordCommandValidator(),
            new ResetPasswordCommand("ada@example.com", "token", weak, weak));

        var change = Failures(
            new ChangePasswordCommandValidator(),
            new ChangePasswordCommand("Old!Passw0rd", weak, weak));

        var strengthMessages = new[]
        {
            "Password must be at least 8 characters long",
            "Password must contain at least one uppercase letter",
            "Password must contain at least one digit",
            "Password must contain at least one special character",
        };

        // Asserted as a set on each path rather than "signup equals change", which would also pass
        // if all three drifted together to the same wrong text.
        signup.Should().Contain(strengthMessages);
        reset.Should().Contain(strengthMessages);
        change.Should().Contain(strengthMessages);

        // The old wording is gone. This is the half the frontend cares about: `passwordPolicy.ts`
        // mirrors ONE vocabulary, so a second one could only ever be matched by one of them.
        change.Should().NotContain(m => m.StartsWith("New password must", StringComparison.Ordinal));
    }

    [Fact]
    public void RequirednessStaysPerCallsite_BecausePasswordRulesDeliberatelySaysNothingAboutIt()
    {
        // #290's distinction, re-pinned here because #292 moved this validator onto the shared
        // rules and the two "required" messages are what tell the change-password fields apart.
        var change = Failures(
            new ChangePasswordCommandValidator(),
            new ChangePasswordCommand(string.Empty, string.Empty, string.Empty));

        change.Should().Contain("Current password is required");
        change.Should().Contain("New password is required");
    }
}
