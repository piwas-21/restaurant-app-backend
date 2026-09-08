using FluentAssertions;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.ApplyGlobalIngredientTranslationsCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// The refusals of the bulk translation apply, pinned on the validator directly: every one of them
/// is a client bug a 400 must name before the handler loads a library row, and the duplicate-
/// language rule is the one an averaging implementation would silently survive.
/// </summary>
/// <remarks>
/// The ten-locale cap mirrors <c>AttachGlobalIngredientCommandValidator</c>'s 500-product cap — a
/// sanity ceiling, not a business rule. There is deliberately NO supported-language membership
/// check: the write path this mirrors (<c>CreateGlobalIngredientCommand</c>) never restricted
/// codes, and this endpoint must not be stricter than the editor it serves.
/// </remarks>
public class ApplyGlobalIngredientTranslationsValidatorTests
{
    private readonly ApplyGlobalIngredientTranslationsCommandValidator _validator = new();

    private static ApplyGlobalIngredientTranslationsCommand BuildCommand(
        List<GlobalIngredientTranslationDto>? translations = null,
        Guid? id = null) => new(
            id ?? Guid.NewGuid(),
            translations ?? [new() { LanguageCode = "fr", Name = "Mozzarella" }]);

    [Fact]
    public void Validate_AValidPayload_Passes()
    {
        var translations = Enumerable.Range(0, 10)
            .Select(i => new GlobalIngredientTranslationDto { LanguageCode = $"l{i}", Name = $"Name {i}" })
            .ToList();

        _validator.Validate(BuildCommand(translations)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AnEmptyGuidId_Fails()
    {
        var result = _validator.Validate(BuildCommand(id: Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Id");
    }

    [Fact]
    public void Validate_AnEmptyTranslationList_Fails()
    {
        var result = _validator.Validate(BuildCommand(translations: []));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Translations");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ABlankLanguageCode_Fails(string languageCode)
    {
        var result = _validator.Validate(BuildCommand(
            translations: [new() { LanguageCode = languageCode, Name = "Mozzarella" }]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("LanguageCode"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ABlankName_Fails(string name)
    {
        var result = _validator.Validate(BuildCommand(
            translations: [new() { LanguageCode = "fr", Name = name }]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Name"));
    }

    [Fact]
    public void Validate_DuplicateLanguages_Fail_EvenInDifferentCase()
    {
        var result = _validator.Validate(BuildCommand(
            translations: new List<GlobalIngredientTranslationDto>
            {
                new() { LanguageCode = "fr", Name = "Mozzarella" },
                new() { LanguageCode = "FR", Name = "Mozzarella di bufala" },
            }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("only once"));
    }

    [Fact]
    public void Validate_MoreThanTenTranslations_Fails()
    {
        var translations = Enumerable.Range(0, 11)
            .Select(i => new GlobalIngredientTranslationDto { LanguageCode = $"l{i}", Name = $"Name {i}" })
            .ToList();

        var result = _validator.Validate(BuildCommand(translations));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("at most 10"));
    }
}
