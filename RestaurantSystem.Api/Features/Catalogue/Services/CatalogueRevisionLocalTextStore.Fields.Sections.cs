using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed partial class CatalogueRevisionLocalTextStore
{
    private async Task<Guid?> SetSectionFieldAsync(
        CatalogueTemplateAdoption adoption,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        string sectionKey,
        string property,
        string locale,
        CentralCatalogueTemplateRevision revision,
        CancellationToken cancellationToken)
    {
        var mapping = RequireSectionMapping(entryMappings, sectionKey);
        var section = await LoadMappedSectionAsync(adoption, mapping, cancellationToken);
        var source = FindSourceSection(revision, sectionKey);
        if (property == "name") return SetSectionName(section, source);
        return await SetSectionTranslationAsync(section, source, locale, cancellationToken);
    }

    private static CatalogueTemplateAdoption RequireSectionMapping(
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        string sectionKey) => entryMappings.FirstOrDefault(value => value.LocalEntityType == "MenuSection" &&
        value.SourceEntryId == sectionKey)
        ?? throw new ConflictException("The adopted bundle section mapping is missing.");

    private async Task<MenuSection> LoadMappedSectionAsync(
        CatalogueTemplateAdoption adoption,
        CatalogueTemplateAdoption mapping,
        CancellationToken cancellationToken)
    {
        var product = await context.Products.Include(value => value.MenuDefinition)
            .FirstOrDefaultAsync(value => value.Id == adoption.LocalEntityId, cancellationToken);
        if (product?.MenuDefinition is null)
        {
            throw new ConflictException("The mapped bundle or its menu no longer exists.");
        }

        var section = await context.MenuSections.FirstOrDefaultAsync(
            value => value.Id == mapping.LocalEntityId, cancellationToken);
        if (section is null)
        {
            throw new ConflictException("The adopted bundle section no longer exists.");
        }

        if (section.MenuDefinitionId != product.MenuDefinition.Id)
        {
            throw new ConflictException("The adopted section no longer belongs to the mapped bundle.");
        }

        return section;
    }

    private static CatalogueBundleSection FindSourceSection(
        CentralCatalogueTemplateRevision revision,
        string sectionKey) => CatalogueImportPayloadReader.ReadBundleSections(revision)
        .FirstOrDefault(value => value.Key == sectionKey)
        ?? throw new ConflictException("The source bundle section is no longer present.");

    private Guid? SetSectionName(MenuSection section, CatalogueBundleSection source)
    {
        ValidateText(source.Name, "bundle section name", 100, required: true);
        if (string.Equals(section.Name, source.Name, StringComparison.Ordinal)) return null;
        section.Name = source.Name;
        Touch(section);
        return section.MenuDefinitionId;
    }

    private async Task<Guid?> SetSectionTranslationAsync(
        MenuSection section,
        CatalogueBundleSection source,
        string locale,
        CancellationToken cancellationToken)
    {
        var value = source.Translations.TryGetValue(locale, out var name) ? name : null;
        var translation = await context.MenuSectionTranslations.FirstOrDefaultAsync(
            item => item.MenuSectionId == section.Id && item.LanguageCode == locale, cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) return RemoveSectionTranslation(section, translation);
        if (translation is null) return AddSectionTranslation(section, locale, value);
        return UpdateSectionTranslation(section, translation, value);
    }

    private Guid? RemoveSectionTranslation(MenuSection section, MenuSectionTranslation? translation)
    {
        if (translation is null) return null;
        context.MenuSectionTranslations.Remove(translation);
        Touch(section);
        return section.MenuDefinitionId;
    }

    private Guid AddSectionTranslation(MenuSection section, string locale, string value)
    {
        ValidateText(value, "bundle section translation name", 100, required: true);
        context.MenuSectionTranslations.Add(new MenuSectionTranslation
        {
            Id = Guid.NewGuid(),
            MenuSectionId = section.Id,
            LanguageCode = locale,
            Name = value,
            CreatedBy = currentUser.GetAuditIdentifier()
        });
        Touch(section);
        return section.MenuDefinitionId;
    }

    private Guid? UpdateSectionTranslation(
        MenuSection section,
        MenuSectionTranslation translation,
        string value)
    {
        if (string.Equals(translation.Name, value, StringComparison.Ordinal)) return null;
        ValidateText(value, "bundle section translation name", 100, required: true);
        translation.Name = value;
        Touch(translation);
        Touch(section);
        return section.MenuDefinitionId;
    }
}
