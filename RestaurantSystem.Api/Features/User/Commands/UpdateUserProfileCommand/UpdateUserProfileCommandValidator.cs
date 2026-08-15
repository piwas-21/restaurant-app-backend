using FluentValidation;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common;

namespace RestaurantSystem.Api.Features.User.Commands.UpdateUserProfileCommand;

public class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
    /// <param name="languages">
    /// Validated against what THIS TENANT sells in, not against the product's ten: on a tenant
    /// configured for `en,fr` a PUT of `zh` would otherwise answer 200 and then be dropped at send
    /// time — a setting that neither sticks nor complains, which is the worse of the two failures.
    /// </param>
    public UpdateUserProfileCommandValidator(IEmailLanguageResolver languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters");

        RuleFor(x => x.PreferredLanguage)
            .Must(language => LanguageCode.Normalize(language) is { } code
                && languages.SupportedLanguages.Contains(code))
            .When(x => !string.IsNullOrWhiteSpace(x.PreferredLanguage))
            .WithMessage("Unsupported language");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{1,14}$").When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Invalid phone number format");
    }
}
