using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed partial class CatalogueRevisionLocalTextStore
{
    private async Task<bool> SetTranslationAsync(
        CatalogueTemplateAdoption adoption,
        string templateType,
        string locale,
        string field,
        CentralCatalogueTemplateRevision revision,
        CancellationToken cancellationToken)
    {
        var hasValue = revision.Translations.TryGetValue(locale, out var text);
        string? value = null;
        if (hasValue)
        {
            value = field == "name" ? text!.Name : text!.Description;
        }
        if (templateType == "option-set" && field == "name") ValidateText(value, "option-set translation name", 120, required: false);
        if (templateType == "ingredient" && field == "name")
        {
            var entity = await context.GlobalIngredientTranslations.FirstOrDefaultAsync(
                item => item.GlobalIngredientId == adoption.LocalEntityId && item.LanguageCode == locale,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (entity is not null)
                {
                    context.GlobalIngredientTranslations.Remove(entity);
                    return true;
                }
            }
            else if (entity is null)
            {
                context.GlobalIngredientTranslations.Add(new GlobalIngredientTranslation
                {
                    Id = Guid.NewGuid(),
                    GlobalIngredientId = adoption.LocalEntityId,
                    LanguageCode = locale,
                    Name = value,
                    CreatedBy = currentUser.GetAuditIdentifier()
                });
                return true;
            }
            else if (!string.Equals(entity.Name, value, StringComparison.Ordinal))
            {
                entity.Name = value;
                Touch(entity);
                return true;
            }
        }
        else if (templateType is "item" or "bundle")
        {
            var entity = await context.ProductDescriptions.FirstOrDefaultAsync(
                item => item.ProductId == adoption.LocalEntityId && item.Lang == locale, cancellationToken);
            if (entity is null && value is not null)
            {
                if (field == "description")
                {
                    throw new ConflictException("Select the matching translation name before adding a translation description.");
                }

                entity = new ProductDescription
                {
                    Id = Guid.NewGuid(),
                    ProductId = adoption.LocalEntityId,
                    Lang = locale,
                    Name = string.Empty,
                    Description = string.Empty,
                    CreatedBy = currentUser.GetAuditIdentifier()
                };
                context.ProductDescriptions.Add(entity);
            }

            if (entity is not null)
            {
                if (value is null && field == "name")
                {
                    if (!string.IsNullOrWhiteSpace(entity.Description))
                    {
                        throw new ConflictException(
                            "The source removed this translation name, but the tenant description has local text. Edit or clear it first.");
                    }

                    context.ProductDescriptions.Remove(entity);
                    return true;
                }

                if (field == "name" && !string.Equals(entity.Name, value, StringComparison.Ordinal))
                {
                    entity.Name = value!;
                    Touch(entity);
                    return true;
                }

                if (field == "description")
                {
                    var description = value ?? string.Empty;
                    if (!string.Equals(entity.Description, description, StringComparison.Ordinal))
                    {
                        entity.Description = description;
                        Touch(entity);
                        return true;
                    }
                }
            }
        }
        else if (templateType == "option-set" && field == "name")
        {
            var entity = await context.OptionSetTranslations.FirstOrDefaultAsync(
                item => item.OptionSetId == adoption.LocalEntityId && item.LanguageCode == locale, cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (entity is not null)
                {
                    context.OptionSetTranslations.Remove(entity);
                    return true;
                }
            }
            else if (entity is null)
            {
                context.OptionSetTranslations.Add(new OptionSetTranslation
                {
                    Id = Guid.NewGuid(),
                    OptionSetId = adoption.LocalEntityId,
                    LanguageCode = locale,
                    Name = value,
                    CreatedBy = currentUser.GetAuditIdentifier()
                });
                return true;
            }
            else if (!string.Equals(entity.Name, value, StringComparison.Ordinal))
            {
                entity.Name = value;
                Touch(entity);
                return true;
            }
        }

        return false;
    }

    private async Task<Guid?> SetSectionFieldAsync(
        CatalogueTemplateAdoption adoption,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        string sectionKey,
        string property,
        string locale,
        CentralCatalogueTemplateRevision revision,
        CancellationToken cancellationToken)
    {
        var mapping = entryMappings.FirstOrDefault(value => value.LocalEntityType == "MenuSection" &&
            value.SourceEntryId == sectionKey);
        if (mapping is null)
        {
            throw new ConflictException("The adopted bundle section mapping is missing.");
        }

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

        var source = CatalogueImportPayloadReader.ReadBundleSections(revision)
            .FirstOrDefault(value => value.Key == sectionKey);
        if (source is null) throw new ConflictException("The source bundle section is no longer present.");

        if (property == "name")
        {
            ValidateText(source.Name, "bundle section name", 100, required: true);
            if (string.Equals(section.Name, source.Name, StringComparison.Ordinal)) return null;
            section.Name = source.Name;
            Touch(section);
            return section.MenuDefinitionId;
        }

        var value = source.Translations.TryGetValue(locale, out var name) ? name : null;
        var translation = await context.MenuSectionTranslations.FirstOrDefaultAsync(
            item => item.MenuSectionId == section.Id && item.LanguageCode == locale, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (translation is not null)
            {
                context.MenuSectionTranslations.Remove(translation);
                Touch(section);
                return section.MenuDefinitionId;
            }
        }
        else if (translation is null)
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
        else if (!string.Equals(translation.Name, value, StringComparison.Ordinal))
        {
            ValidateText(value, "bundle section translation name", 100, required: true);
            translation.Name = value;
            Touch(translation);
            Touch(section);
            return section.MenuDefinitionId;
        }

        return null;
    }

    private void Touch(BaseEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.GetAuditIdentifier();
    }

    private bool SetText(
        BaseEntity entity,
        string? current,
        string? value,
        Action<string?> setter,
        string label,
        int? maxLength)
    {
        ValidateText(value, label, maxLength, required: label.EndsWith("name", StringComparison.Ordinal));
        if (string.Equals(current, value, StringComparison.Ordinal)) return false;
        setter(value);
        Touch(entity);
        return true;
    }

    private static void ValidateText(string? value, string label, int? maxLength, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || (maxLength is int maximum && value?.Length > maximum))
        {
            throw new ConflictException($"The published {label} cannot be applied to the tenant record.");
        }
    }
}
