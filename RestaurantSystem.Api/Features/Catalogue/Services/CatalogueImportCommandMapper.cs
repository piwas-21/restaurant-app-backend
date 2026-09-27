using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.CreateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportCommandMapper
{
    public static CreateCategoryCommand Category(
        CentralCatalogueTemplateRevision revision,
        string locale,
        CatalogueImportItemDecision decision)
    {
        var localized = CatalogueSessionMapper.Localized(revision, locale);
        var translations = CategoryContent(revision, locale, decision, localized);
        return new CreateCategoryCommand(
            Name: LocalName(decision, localized.Name),
            Description: decision.LocalDescription ?? localized.Description,
            IsActive: true,
            DisplayOrder: CatalogueImportPayloadReader.ReadInt(revision.Payload, "sortOrder"),
            Translations: translations,
            SourceLocale: locale,
            TranslationMetadata: CatalogueImportCategoryTranslationMapper.OwnerMetadata(revision, locale));
    }

    private static Dictionary<string, CategoryContentDto> CategoryContent(
        CentralCatalogueTemplateRevision revision,
        string locale,
        CatalogueImportItemDecision decision,
        (string Name, string? Description) localized)
    {
        var content = new Dictionary<string, CategoryContentDto>(StringComparer.OrdinalIgnoreCase)
        {
            [revision.SourceLocale] = new CategoryContentDto
            {
                Name = revision.Name.Trim(),
                Description = revision.Description
            }
        };
        foreach (var (language, translation) in revision.Translations)
        {
            if (language.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase)) continue;
            content[language] = new CategoryContentDto
            {
                Name = translation.Name,
                Description = translation.Description
            };
        }

        if (decision.LocalName is not null || decision.LocalDescription is not null ||
            !content.ContainsKey(locale))
        {
            content.TryGetValue(locale, out var current);
            content[locale] = new CategoryContentDto
            {
                Name = decision.LocalName?.Trim() ?? current?.Name ?? localized.Name,
                Description = decision.LocalDescription ?? current?.Description ?? localized.Description
            };
        }

        return content;
    }

    public static CreateGlobalIngredientCommand Ingredient(
        CentralCatalogueTemplateRevision revision,
        string locale,
        CatalogueImportItemDecision decision)
    {
        var role = CatalogueImportPayloadReader.ReadIngredientRole(revision);
        var kind = role switch
        {
            "ingredient" => IngredientKind.Ingredient,
            "sauce" => IngredientKind.Sauce,
            _ => throw CatalogueImportPayloadReader.Unsupported(
                "This ingredient role cannot be represented by the tenant ingredient library.",
                "UNSUPPORTED_INGREDIENT_ROLE")
        };

        var defaultName = revision.Name.Trim();
        var translations = revision.Translations
            .Where(pair => !pair.Key.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase);
        var localName = decision.LocalName?.Trim();
        if (!string.IsNullOrWhiteSpace(localName))
        {
            if (locale.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase))
            {
                defaultName = localName;
            }
            else
            {
                translations[locale] = localName;
            }
        }

        return new CreateGlobalIngredientCommand(
            defaultName,
            ImageUrl: null,
            Translations: translations.Select(pair => new GlobalIngredientTranslationDto
            {
                LanguageCode = pair.Key,
                Name = pair.Value
            }).ToList(),
            Kind: kind);
    }

    public static CreateProductCommand Item(
        CentralCatalogueTemplateRevision revision,
        string locale,
        CatalogueImportItemDecision decision,
        Guid categoryId,
        List<ProductCustomizationGroupDto>? customizationGroups = null)
    {
        var localized = CatalogueSessionMapper.Localized(revision, locale);
        var content = ProductContent(revision, locale, decision, localized);
        return new CreateProductCommand(
            Name: LocalName(decision, localized.Name),
            Description: decision.LocalDescription ?? localized.Description,
            BasePrice: decision.LocalPrice ?? 0,
            IsActive: false,
            IsAvailable: false,
            IsSpecial: false,
            PreparationTimeMinutes: 0,
            Type: ParseProductType(decision.LocalProductType),
            KitchenType: decision.KitchenType ?? throw new BadRequestException("Kitchen type review is required"),
            Ingredients: RequireReviewedList(decision.Ingredients, "ingredients"),
            Allergens: RequireReviewedList(decision.Allergens, "allergens"),
            DisplayOrder: 0,
            CategoryIds: [categoryId],
            PrimaryCategoryId: categoryId,
            Variations: null,
            SuggestedSideItemIds: null,
            DetailedIngredients: null,
            Content: content,
            AvailableOrderTypes: decision.AvailableOrderTypes,
            CustomizationGroups: customizationGroups,
            TranslationMetadata: CatalogueImportTranslationMapper.OwnerMetadata(revision));
    }

    public static CreateMenuBundleCommand Bundle(
        CentralCatalogueTemplateRevision revision,
        string locale,
        CatalogueImportItemDecision decision,
        IReadOnlyList<MenuSectionDto> sections,
        Guid? parentOfferProductId)
    {
        var localized = CatalogueSessionMapper.Localized(revision, locale);
        var definition = new MenuDefinitionDto
        {
            IsAlwaysAvailable = true,
            AvailableMonday = true,
            AvailableTuesday = true,
            AvailableWednesday = true,
            AvailableThursday = true,
            AvailableFriday = true,
            AvailableSaturday = true,
            AvailableSunday = true,
            Sections = sections.ToList()
        };
        if (parentOfferProductId.HasValue)
        {
            definition = definition with { ParentOfferProductId = parentOfferProductId };
        }

        return new CreateMenuBundleCommand(
            Name: LocalName(decision, localized.Name),
            Description: decision.LocalDescription ?? localized.Description,
            BasePrice: decision.LocalPrice ?? throw new BadRequestException("A tenant-local bundle price is required"),
            IsActive: false,
            IsAvailable: false,
            IsSpecial: false,
            PreparationTimeMinutes: 0,
            DisplayOrder: 0,
            CategoryIds: null,
            PrimaryCategoryId: null,
            MenuDefinition: definition,
            Content: ProductContent(revision, locale, decision, localized),
            AvailableOrderTypes: decision.AvailableOrderTypes,
            Allergens: RequireReviewedList(decision.Allergens, "allergens"),
            TranslationMetadata: CatalogueImportTranslationMapper.OwnerMetadata(revision));
    }

    private static List<string> RequireReviewedList(List<string>? values, string fieldName) =>
        values?.Select(value => value.Trim()).ToList()
        ?? throw new BadRequestException(
            $"The tenant-reviewed {fieldName} list must be explicit. Use an empty list to confirm there are no values.",
            $"TENANT_{fieldName.ToUpperInvariant()}_REQUIRED");

    private static ProductDescriptionsDto ProductContent(
        CentralCatalogueTemplateRevision revision,
        string locale,
        CatalogueImportItemDecision decision,
        (string Name, string? Description) localized)
    {
        var content = new ProductDescriptionsDto
        {
            [revision.SourceLocale] = new ProductDescriptionDto
            {
                Name = revision.Name.Trim(),
                Description = revision.Description ?? string.Empty
            }
        };
        foreach (var (language, translation) in revision.Translations)
        {
            if (language.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            content[language] = new ProductDescriptionDto
            {
                Name = translation.Name,
                Description = translation.Description ?? string.Empty
            };
        }

        if (decision.LocalName is not null || decision.LocalDescription is not null)
        {
            content.TryGetValue(locale, out var current);
            content[locale] = new ProductDescriptionDto
            {
                Name = decision.LocalName?.Trim() ?? current?.Name ?? localized.Name,
                Description = decision.LocalDescription ?? current?.Description ?? localized.Description ?? string.Empty
            };
        }

        return content;
    }

    private static ProductType ParseProductType(string? value) => value switch
    {
        "MainItem" => ProductType.MainItem,
        "Beverage" => ProductType.Beverage,
        "Dessert" => ProductType.Dessert,
        "Sauce" => ProductType.Sauce,
        "AddOn" => ProductType.AddOn,
        _ => throw new BadRequestException("A supported tenant product type is required")
    };

    private static string LocalName(CatalogueImportItemDecision decision, string fallback) =>
        string.IsNullOrWhiteSpace(decision.LocalName) ? fallback : decision.LocalName.Trim();
}
