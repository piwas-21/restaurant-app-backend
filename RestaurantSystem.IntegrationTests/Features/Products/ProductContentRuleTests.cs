using FluentAssertions;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;
using RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuBundleCommand;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// #306. FOUR handlers build ProductDescription rows out of the same ProductDescriptionsDto, and none
// of them validated it. ProductUpdateContentTests drives the real endpoint for ONE of them; this
// pins the other three, because "the shared rule exists" and "every validator applies it" are
// different claims and only the second one is worth anything.
//
// That distinction is not hypothetical here: the same dead duplicate-language-code check was once
// pasted into all four of these handlers and had to be removed from each separately (#192, #193). A
// rule that lands on three of four fails exactly the same way, silently.
public class ProductContentRuleTests
{
    private static ProductDescriptionsDto Content(string lang, ProductDescriptionDto? entry) =>
        new() { [lang] = entry! };

    private static ProductDescriptionsDto Valid() =>
        Content("en", new ProductDescriptionDto { Name = "n", Description = "d" });

    private static readonly MenuDefinitionDto EmptyMenu = new() { Sections = new List<MenuSectionDto>() };

    private static CreateProductCommand CreateProduct(ProductDescriptionsDto? content)
    {
        var categoryId = Guid.NewGuid();
        return new CreateProductCommand(
            Name: "p", Description: null, BasePrice: 1m, IsActive: true, IsAvailable: true,
            IsSpecial: false, PreparationTimeMinutes: 1, Type: ProductType.MainItem,
            KitchenType: KitchenType.None, Ingredients: null, Allergens: null, DisplayOrder: 0,
            CategoryIds: new List<Guid> { categoryId }, PrimaryCategoryId: categoryId,
            Variations: null, SuggestedSideItemIds: null, DetailedIngredients: null,
            Content: content!);
    }

    private static UpdateProductCommand UpdateProduct(ProductDescriptionsDto? content)
    {
        var categoryId = Guid.NewGuid();
        return new UpdateProductCommand(
            Id: Guid.NewGuid(), Name: "p", Description: null, BasePrice: 1m, IsActive: true,
            IsAvailable: true, IsSpecial: false, PreparationTimeMinutes: 1,
            Type: ProductType.MainItem, KitchenType: KitchenType.None, Ingredients: null,
            Allergens: null, DisplayOrder: 0, CategoryIds: new List<Guid> { categoryId },
            PrimaryCategoryId: categoryId, Variations: null, SuggestedSideItemIds: null,
            DetailedIngredients: null, MenuDefinition: null, Content: content);
    }

    private static CreateMenuBundleCommand CreateBundle(ProductDescriptionsDto? content) =>
        new(Name: "b", Description: null, BasePrice: 1m, IsActive: true, IsAvailable: true,
            IsSpecial: false, PreparationTimeMinutes: 1, DisplayOrder: 0, CategoryIds: null,
            PrimaryCategoryId: null, MenuDefinition: EmptyMenu, Content: content!);

    private static UpdateMenuBundleCommand UpdateBundle(ProductDescriptionsDto? content) =>
        new(Id: Guid.NewGuid(), Name: "b", Description: null, BasePrice: 1m, IsActive: true,
            IsAvailable: true, IsSpecial: false, PreparationTimeMinutes: 1, DisplayOrder: 0,
            CategoryIds: null, PrimaryCategoryId: null, MenuDefinition: EmptyMenu, Content: content!);

    /// <summary>
    /// Validates a command through its REAL validator and returns the error messages. Each case runs
    /// the same malformed content through all four, so a validator that never got the rule shows up
    /// as one entry with no errors rather than as a whole test class nobody wrote.
    /// </summary>
    private static IEnumerable<(string Validator, List<string> Errors)> ValidateAll(ProductDescriptionsDto? content)
    {
        yield return ("CreateProduct",
            new CreateProductCommandValidator().Validate(CreateProduct(content)).Errors.Select(e => e.ErrorMessage).ToList());
        yield return ("UpdateProduct",
            new UpdateProductCommandValidator().Validate(UpdateProduct(content)).Errors.Select(e => e.ErrorMessage).ToList());
        yield return ("CreateMenuBundle",
            new CreateMenuBundleCommandValidator().Validate(CreateBundle(content)).Errors.Select(e => e.ErrorMessage).ToList());
        yield return ("UpdateMenuBundle",
            new UpdateMenuBundleCommandValidator().Validate(UpdateBundle(content)).Errors.Select(e => e.ErrorMessage).ToList());
    }

    public static TheoryData<string, string> MalformedContent() => new()
    {
        { "missing-description", ProductContentRule.DescriptionRequiredMessage },
        { "missing-name", ProductContentRule.NameRequiredMessage },
        // #325. A blank name is refused on all four paths, not just a null one — the same
        // IsNullOrWhiteSpace test NestedContentRule already applies to the nested maps. Both the
        // empty and the whitespace-only spelling are listed because they arrive by different
        // routes and a `== string.Empty` guard would catch only one of them.
        { "empty-name", ProductContentRule.NameRequiredMessage },
        { "whitespace-name", ProductContentRule.NameRequiredMessage },
        { "null-entry", ProductContentRule.EntryRequiredMessage },
        { "blank-key", ProductContentRule.LanguageKeyRequiredMessage },
    };

    [Theory]
    [MemberData(nameof(MalformedContent))]
    public void EveryValidator_RejectsMalformedContent(string shape, string expectedMessage)
    {
        var content = shape switch
        {
            "missing-description" => Content("en", new ProductDescriptionDto { Name = "n", Description = null! }),
            "missing-name" => Content("en", new ProductDescriptionDto { Name = null!, Description = "d" }),
            "empty-name" => Content("en", new ProductDescriptionDto { Name = string.Empty, Description = "d" }),
            "whitespace-name" => Content("en", new ProductDescriptionDto { Name = "   ", Description = "d" }),
            "null-entry" => Content("en", null),
            "blank-key" => Content("   ", new ProductDescriptionDto { Name = "n", Description = "d" }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        foreach (var (validator, errors) in ValidateAll(content))
        {
            // ContainSingle, not Contain. A validator that registers the rule twice — the shared
            // base AND its own derived class, which AbstractValidator happily appends rather than
            // dedupes — reports every message twice, and a Contain assertion is satisfied by both.
            // That is exactly what an earlier draft of this fix did to the bundle create path.
            errors.Should().ContainSingle(e => e.StartsWith(expectedMessage, StringComparison.Ordinal),
                $"{validator} must reject a '{shape}' translation exactly once");
        }
    }

    // An OVER-STRICTNESS guard, not a rule-presence guard: it passes unchanged if ValidProductContent
    // is gutted to a no-op, so it proves nothing about wiring — EveryValidator_RejectsMalformedContent
    // is what does that. Its job is the opposite direction: the rule must not fire on a well-formed
    // map, or every save from the admin editor would 400. Asserted as "no CONTENT error" rather than
    // IsValid, because these skeleton commands trip other, unrelated rules (a bundle with no
    // sections, say) that this rule has nothing to do with.
    [Fact]
    public void NoValidator_RejectsWellFormedContent()
    {
        var contentMessages = new[]
        {
            ProductContentRule.DescriptionRequiredMessage,
            ProductContentRule.NameRequiredMessage,
            ProductContentRule.EntryRequiredMessage,
            ProductContentRule.LanguageKeyRequiredMessage
        };

        foreach (var (validator, errors) in ValidateAll(Valid()))
        {
            errors.Should().NotContain(e => contentMessages.Any(m => e.StartsWith(m, StringComparison.Ordinal)),
                $"{validator} must accept a well-formed translation map");
        }
    }

    // Also an over-strictness guard (see above — it survives a gutted rule). An empty DESCRIPTION is
    // legitimate: the admin form posts `description: data.description || ''` on every save, so this
    // is what stops Description being "tidied" into NotEmpty, which would 400 an ordinary edit of a
    // product that simply has no description text.
    //
    // The NAME half of this guard was deleted by #325 and now lives, inverted, in
    // MalformedContent above. That is a deliberate narrowing, not a lost assertion: an empty name
    // is not the same kind of value as an empty description. A description is a field the admin may
    // legitimately leave blank; a name is the whole content of the row, and a row with only a
    // description is the half-row this rule now refuses. The permissive claim about Description is
    // unchanged, and this test is what keeps it from being widened back over Name by accident.
    [Fact]
    public void NoValidator_RejectsAnEmptyDescription()
    {
        var content = Content("en", new ProductDescriptionDto { Name = "n", Description = string.Empty });

        foreach (var (validator, errors) in ValidateAll(content))
        {
            errors.Should().NotContain(e =>
                e.StartsWith(ProductContentRule.NameRequiredMessage, StringComparison.Ordinal) ||
                e.StartsWith(ProductContentRule.DescriptionRequiredMessage, StringComparison.Ordinal),
                $"{validator} must accept an empty-but-present description");
        }
    }

    /// <summary>
    /// The release-order evidence for #325, as a test rather than as a sentence in a comment.
    /// </summary>
    /// <remarks>
    /// NestedContentRule had to wait for frontend #450 because the shipped editor really did post
    /// blank-named NESTED entries, and landing the server rule first would have 400'd every
    /// ordinary save. The top-level rule has no such wait, and this pins the reason.
    ///
    /// The editor builds the top-level map in exactly two places, both in `productFormUtils.ts`:
    /// the EDIT path filters on `e?.language?.trim() &amp;&amp; e?.name?.trim()`, so a blank-named row is
    /// already dropped before the wire; the CREATE path seeds `content[currentLanguage].name` from
    /// the product's OWN name, untrimmed, and filters every other row on `item.name?.trim()`. So
    /// the single way the shipped editor could send a blank top-level name is a product whose own
    /// name is whitespace — and that request was ALREADY refused, by `RuleFor(x => x.Name).NotEmpty()`,
    /// before this rule existed. FluentValidation's NotEmpty rejects whitespace, which is the load-
    /// bearing fact and is therefore asserted here rather than assumed from its name.
    /// </remarks>
    [Fact]
    public void AWhitespaceProductName_IsAlreadyRefused_SoTheCreatePathCannotSendABlankTranslationName()
    {
        var create = CreateProduct(Valid()) with { Name = "   " };
        var update = UpdateProduct(Valid()) with { Name = "   " };

        new CreateProductCommandValidator().Validate(create).Errors
            .Should().Contain(e => e.ErrorMessage == "Product name is required");
        new UpdateProductCommandValidator().Validate(update).Errors
            .Should().Contain(e => e.ErrorMessage == "Name is required");
    }

    // The CREATE handlers dereference `command.Content` with no coalesce, so a null map threw
    // ArgumentNullException and leaked "Value cannot be null. (Parameter 'source')". The UPDATE
    // handlers coalesce, where an absent map legitimately means "no translation changes" (#190) —
    // requiring it there would 400 every save that does not touch translations. The asymmetry is
    // deliberate and is pinned in BOTH directions, because it looks like an oversight.
    [Fact]
    public void OnlyTheCreateValidators_RequireContentToBePresent()
    {
        var byValidator = ValidateAll(null).ToDictionary(x => x.Validator, x => x.Errors);

        byValidator["CreateProduct"].Should().ContainSingle(e => e == ProductContentRule.ContentRequiredMessage);
        byValidator["CreateMenuBundle"].Should().ContainSingle(e => e == ProductContentRule.ContentRequiredMessage);
        byValidator["UpdateProduct"].Should().NotContain(ProductContentRule.ContentRequiredMessage);
        byValidator["UpdateMenuBundle"].Should().NotContain(ProductContentRule.ContentRequiredMessage);
    }
}
