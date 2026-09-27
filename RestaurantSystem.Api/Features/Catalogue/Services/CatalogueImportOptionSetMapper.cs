using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportOptionSetMapper
{
    public static CreateOrReuseImportedSetRequest ForTemplate(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportSession session,
        CatalogueOptionSetPayload payload,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver)
    {
        var kind = ParseKind(payload.Kind);
        var name = revision.Name.Trim();
        var translations = revision.Translations
            .Where(pair => !pair.Key.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase);
        ApplyLocalName(decision.LocalName, session.Locale, revision.SourceLocale, ref name, translations);

        return new CreateOrReuseImportedSetRequest
        {
            SourceTemplateId = revision.TemplateId,
            SourceRevision = revision.Revision,
            SourceOptionSetId = "default",
            Kind = kind,
            Name = name,
            SourceLocale = revision.SourceLocale,
            Translations = translations,
            TranslationMetadata = CatalogueImportTranslationMapper.OwnerMetadata(revision, includeDescription: false),
            Entries = BuildEntries(payload.Options, kind, session, resolver, decision),
            StagedProductIds = StagedProductIds(session)
        };
    }

    public static CreateOrReuseImportedSetRequest ForBundleSection(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportSession session,
        CatalogueBundleSection section,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver)
    {
        var translations = new Dictionary<string, string>(section.Translations, StringComparer.OrdinalIgnoreCase);

        return new CreateOrReuseImportedSetRequest
        {
            SourceTemplateId = revision.TemplateId,
            SourceRevision = revision.Revision,
            SourceOptionSetId = section.Key,
            Kind = OptionSetKind.BundleChoice,
            Name = section.Name,
            SourceLocale = revision.SourceLocale,
            Translations = translations,
            TranslationMetadata = CatalogueImportTranslationMapper.OwnerMetadata(revision, includeDescription: false),
            Entries = BuildEntries(section.Options, OptionSetKind.BundleChoice, session, resolver, decision),
            StagedProductIds = StagedProductIds(session)
        };
    }

    public static ProductCustomizationGroupDto ProductChoiceGroup(
        CentralCatalogueTemplateRevision revision,
        string locale,
        int displayOrder,
        CatalogueImportItemDecision? decision)
    {
        var localized = CatalogueSessionMapper.Localized(revision, locale);
        var content = new Dictionary<string, ProductCustomizationGroupContentDto>(StringComparer.OrdinalIgnoreCase)
        {
            [revision.SourceLocale] = new() { Name = revision.Name.Trim(), Description = revision.Description }
        };
        foreach (var (language, translation) in revision.Translations)
        {
            content[language] = new ProductCustomizationGroupContentDto
            {
                Name = translation.Name,
                Description = translation.Description
            };
        }

        if (!string.IsNullOrWhiteSpace(decision?.LocalName))
        {
            var localName = decision.LocalName.Trim();
            if (locale.Equals(revision.SourceLocale, StringComparison.OrdinalIgnoreCase))
            {
                content[revision.SourceLocale] = new ProductCustomizationGroupContentDto
                {
                    Name = localName,
                    Description = content[revision.SourceLocale].Description
                };
            }
            else
            {
                content[locale] = new ProductCustomizationGroupContentDto
                {
                    Name = localName,
                    Description = content.GetValueOrDefault(locale)?.Description
                };
            }
        }

        var displayName = string.IsNullOrWhiteSpace(decision?.LocalName)
            ? localized.Name
            : decision.LocalName.Trim();
        return new ProductCustomizationGroupDto
        {
            Name = displayName,
            DisplayOrder = displayOrder,
            IsActive = true,
            MinSelection = 0,
            MaxSelection = 0,
            IncludedFreeUnits = 0,
            Content = content
        };
    }

    public static OptionSetKind ParseKind(string kind) => kind switch
    {
        "ingredient" => OptionSetKind.Ingredient,
        "sauce" => OptionSetKind.Sauce,
        "bundle-option" => OptionSetKind.BundleChoice,
        "suggested-side" => OptionSetKind.SuggestedSide,
        _ => throw CatalogueImportPayloadReader.Unsupported("This option-set kind is not supported.")
    };

    public static OptionSetAttachmentRole AttachmentRole(OptionSetKind kind, bool productChoice) => kind switch
    {
        OptionSetKind.Ingredient => OptionSetAttachmentRole.Ingredient,
        OptionSetKind.Sauce => OptionSetAttachmentRole.Sauce,
        OptionSetKind.BundleChoice when productChoice => OptionSetAttachmentRole.ProductChoice,
        OptionSetKind.BundleChoice => OptionSetAttachmentRole.BundleChoice,
        OptionSetKind.SuggestedSide => OptionSetAttachmentRole.SuggestedSide,
        _ => throw new BadRequestException("The imported option-set kind is not supported.", "OPTION_SET_KIND_MISMATCH")
    };

    public static OptionSetAttachmentSettings AttachmentSettings(
        OptionSetKind kind,
        int minimum,
        int maximum,
        int displayOrder) => kind switch
        {
            OptionSetKind.Sauce or OptionSetKind.BundleChoice => new OptionSetAttachmentSettings
            {
                MinSelection = minimum,
                MaxSelection = maximum,
                DisplayOrder = displayOrder
            },
            _ => new OptionSetAttachmentSettings { DisplayOrder = displayOrder }
        };

    public static IReadOnlySet<Guid> StagedProductIds(
        CatalogueImportSession session,
        params Guid[] additionalIds) => session.Templates
            .Where(item => item.IsSelected && item.Status == CatalogueImportItemStatus.Imported
                && item.LocalEntityId.HasValue
                && item.LocalEntityType is "Product" or "MenuBundle")
            .Select(item => item.LocalEntityId!.Value)
            .Concat(additionalIds)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

    public static string StableMaterializationKey(
        Guid adoptionId,
        string templateId,
        int revision,
        string targetKey)
    {
        var value = $"{adoptionId:N}:{templateId}:{revision}:{targetKey}";
        return $"catalogue-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";
    }

    private static List<ImportedOptionSetEntryRequest> BuildEntries(
        IReadOnlyList<CatalogueOptionReference> options,
        OptionSetKind kind,
        CatalogueImportSession session,
        CatalogueImportEntityResolver resolver,
        CatalogueImportItemDecision decision)
    {
        var expectedEntityType = kind is OptionSetKind.Ingredient or OptionSetKind.Sauce
            ? "GlobalIngredient"
            : "Product";
        var entries = options.Select(option => BuildEntry(option, kind, session, resolver, decision, expectedEntityType))
            .ToList();
        var resolvedIds = entries.Select(entry => entry.GlobalIngredientId ?? entry.ProductId).ToArray();
        if (resolvedIds.Distinct().Count() != resolvedIds.Length)
        {
            throw new BadRequestException(
                "Distinct catalogue choices resolve to the same tenant record. Choose distinct tenant records before importing.",
                "DUPLICATE_RESOLVED_OPTION");
        }

        return entries;
    }

    private static ImportedOptionSetEntryRequest BuildEntry(
        CatalogueOptionReference option,
        OptionSetKind kind,
        CatalogueImportSession session,
        CatalogueImportEntityResolver resolver,
        CatalogueImportItemDecision decision,
        string expectedEntityType)
    {
        var localId = resolver.ResolveDependency(session, option.Reference, expectedEntityType);
        var dependency = session.Templates.FirstOrDefault(item =>
            item.TemplateId == option.Reference.TemplateId && item.Revision == option.Reference.Revision);
        var dependencyDecision = CatalogueSessionMapper.ParseDecision(dependency?.DecisionJson);
        var name = dependency is null
            ? option.Reference.TemplateId
            : CreatedDependencyName(dependencyDecision) ?? CatalogueSessionMapper.Localized(
                CatalogueSessionMapper.ParseRevision(dependency.RevisionJson), session.Locale).Name;
        var entry = new ImportedOptionSetEntryRequest
        {
            SourceEntryId = SourceEntryId(option.Reference),
            DisplayOrder = option.SortOrder,
            Name = name,
            GlobalIngredientId = expectedEntityType == "GlobalIngredient" ? localId : null,
            ProductId = expectedEntityType == "Product" ? localId : null,
            IsDefault = option.IsDefault
        };

        if (kind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            entry.IsOptional = true;
            entry.MaxQuantity = 1;
            entry.Price = RequiredLocalPrice(decision, option.Reference);
        }
        else if (kind == OptionSetKind.BundleChoice)
        {
            entry.AdditionalPrice = RequiredLocalPrice(decision, option.Reference);
        }

        return entry;
    }

    private static string? CreatedDependencyName(CatalogueImportItemDecision? decision) =>
        decision?.Resolution.Equals("Create", StringComparison.OrdinalIgnoreCase) == true &&
        !string.IsNullOrWhiteSpace(decision.LocalName)
            ? decision.LocalName.Trim()
            : null;

    private static decimal RequiredLocalPrice(
        CatalogueImportItemDecision decision,
        CatalogueSourceReference reference)
    {
        if (decision.LocalOptionPrices?.TryGetValue(reference.Key, out var price) != true || price < 0)
        {
            throw new BadRequestException(
                $"A non-negative tenant-local price is required for {reference.Key}.",
                "OPTION_PRICE_REQUIRED");
        }

        return price;
    }

    private static string SourceEntryId(CatalogueSourceReference reference)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference.Key))).ToLowerInvariant();
        return $"entry-{hash}";
    }

    private static void ApplyLocalName(
        string? requestedName,
        string locale,
        string sourceLocale,
        ref string name,
        Dictionary<string, string> translations)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return;
        }

        if (locale.Equals(sourceLocale, StringComparison.OrdinalIgnoreCase))
        {
            name = requestedName.Trim();
        }
        else
        {
            translations[locale] = requestedName.Trim();
        }
    }
}
