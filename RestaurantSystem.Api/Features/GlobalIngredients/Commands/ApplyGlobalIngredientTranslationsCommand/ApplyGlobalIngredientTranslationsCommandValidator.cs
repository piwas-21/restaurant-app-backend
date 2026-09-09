using FluentValidation;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Commands.ApplyGlobalIngredientTranslationsCommand;

/// <summary>
/// A bulk write over every copy of a library row is bounded by the same sanity caps as the bulk
/// attach (<c>AttachGlobalIngredientCommandValidator</c>): the app ships exactly ten locales, so a
/// payload larger than that is a client bug, and duplicate language codes are a bug this endpoint
/// cannot silently average over — "update fr twice" has no honest meaning.
/// </summary>
/// <remarks>
/// No membership check against a supported-language list, on purpose: the write path this mirrors
/// (<c>CreateGlobalIngredientCommand</c> / <c>UpdateGlobalIngredientCommand</c>) does not restrict
/// translation codes, so inventing a restriction here would make this endpoint stricter than the
/// editor it serves. A code the frontend cannot render is unreachable from it anyway.
/// </remarks>
public class ApplyGlobalIngredientTranslationsCommandValidator
    : AbstractValidator<ApplyGlobalIngredientTranslationsCommand>
{
    /// <summary>
    /// The app has exactly ten locales (<c>LanguageCode.Supported</c>) — a payload beyond that is
    /// not a translation set but a mistake.
    /// </summary>
    private const int MaxTranslationsPerBatch = 10;

    public ApplyGlobalIngredientTranslationsCommandValidator()
    {
        RuleFor(command => command.Id)
            .NotEmpty().WithMessage("A global ingredient id is required.");

        RuleFor(command => command.Translations)
            .NotEmpty().WithMessage("Provide at least one translation.");

        RuleFor(command => command.Translations)
            .Must(translations => translations.Count <= MaxTranslationsPerBatch)
            .WithMessage($"A bulk apply covers at most {MaxTranslationsPerBatch} translations at a time.");

        RuleForEach(command => command.Translations).ChildRules(translation =>
        {
            translation.RuleFor(entry => entry.LanguageCode)
                .NotEmpty().WithMessage("A language code is required.");
            translation.RuleFor(entry => entry.Name)
                .NotEmpty().WithMessage("A translated name cannot be empty.");
        });

        RuleFor(command => command.Translations)
            .Must(translations => translations
                .GroupBy(entry => entry.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .All(group => group.Count() == 1))
            .WithMessage("Each language may appear only once.");
    }
}
