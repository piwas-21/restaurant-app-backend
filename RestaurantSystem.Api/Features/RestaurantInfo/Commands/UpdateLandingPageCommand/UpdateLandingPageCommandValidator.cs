using FluentValidation;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateLandingPageCommand;

public class UpdateLandingPageCommandValidator : AbstractValidator<UpdateLandingPageCommand>
{
    public UpdateLandingPageCommandValidator(IEmailLanguageResolver languages)
    {
        RuleFor(command => command.BackgroundMode)
            .Must(value => Enum.TryParse<LandingBackgroundMode>(value, true, out _))
            .WithMessage("Background mode must be default, custom, or none.");
        RuleFor(command => command.Content).NotNull();
        RuleForEach(command => command.Content).SetValidator(new LandingContentValidator(languages));
        RuleFor(command => command.Content)
            .Must(content => content is not null && content.Select(item => CanonicalLanguage(item.LanguageCode, languages))
                .Distinct(StringComparer.Ordinal).Count() == content.Count)
            .WithMessage("Duplicate language codes are not allowed.");
    }

    private static string? CanonicalLanguage(string? code, IEmailLanguageResolver languages) =>
        string.IsNullOrWhiteSpace(code) ? languages.TenantDefault : LanguageCode.Normalize(code);

    private sealed class LandingContentValidator : AbstractValidator<UpdateLandingPageContentDto>
    {
        public LandingContentValidator(IEmailLanguageResolver languages)
        {
            RuleFor(item => item.LanguageCode)
                .Must(code => string.IsNullOrWhiteSpace(code) || (LanguageCode.Normalize(code) is { } normalized
                    && languages.SupportedLanguages.Contains(normalized)))
                .WithMessage("Unsupported language");
            RuleFor(item => item.HeroEyebrow).MaximumLength(100);
            RuleFor(item => item.WelcomeTitle).MaximumLength(200);
            RuleFor(item => item.WelcomeBody).MaximumLength(4_000);
            RuleFor(item => item.StoryTitle).MaximumLength(200);
            RuleFor(item => item.StoryBody).MaximumLength(4_000);
        }
    }
}
