using FluentValidation;

namespace RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;

/// <summary>
/// Shape only. Whether the token is genuine is decided by
/// <c>IAppleIdentityTokenVerifier</c>, never here.
/// </summary>
public class AppleLoginCommandValidator : AbstractValidator<AppleLoginCommand>
{
    private const int MaxNameLength = 100;

    public AppleLoginCommandValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty().WithMessage("Identity token is required");

        // Matches UpdateUserProfileCommandValidator, so a name that arrives here can also be
        // edited later through the profile endpoint.
        RuleFor(x => x.FirstName)
            .MaximumLength(MaxNameLength).WithMessage("First name must not exceed 100 characters");

        RuleFor(x => x.LastName)
            .MaximumLength(MaxNameLength).WithMessage("Last name must not exceed 100 characters");
    }
}
