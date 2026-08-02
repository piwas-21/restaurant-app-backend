using FluentValidation;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// The password STRENGTH rules, in one place.
///
/// Separated from "a password is required" on purpose. The two were fused into a single copied
/// chain in four validators, and <c>UpdateStaffCommandValidator</c> — where the password is
/// OPTIONAL — inherited the <c>NotEmpty()</c> along with the strength rules. The result was that an
/// admin editing a staff member's name, email, phone or role without touching the password had the
/// update refused outright, so the edit could not be saved at all (issue #290).
/// Requiredness now belongs to the callsite; strength lives here and is identical everywhere.
///
/// What the refusal actually looked like, since it is easy to guess wrong: the strength rules all
/// PASS on a null value, and the client omits the key entirely rather than sending <c>""</c>, so
/// the response carried exactly ONE message — "Password is required". (An empty string would have
/// produced six and a whitespace-only one five, but no client sends either.)
///
/// The messages are load-bearing beyond this project: the frontend routes them onto form fields by
/// matching their text (<c>apiFormErrors.ts</c>'s <c>STAFF_REGISTRATION_MATCHERS</c>), and
/// <c>lib/passwordPolicy.ts</c> mirrors these rules client-side to refuse a bad password before it
/// is ever sent. Reword one and the matching field goes silent — change both sides together.
///
/// Identity applies its own <see cref="StrongPasswordValidator{TUser}"/> on top when the password
/// actually reaches <c>UserManager</c> (repeat characters, common passwords). These rules are the
/// cheap pre-check that keeps most failures out of that path.
/// </summary>
public static class PasswordRules
{
    /// <summary>Applies the six strength rules. Says nothing about whether a value must be present.</summary>
    public static IRuleBuilderOptions<T, string?> MeetsPasswordPolicy<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character");
}
