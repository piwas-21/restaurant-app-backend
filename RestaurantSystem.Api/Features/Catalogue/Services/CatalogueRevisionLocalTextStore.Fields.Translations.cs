using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
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
        var value = TranslationValue(revision, locale, field);
        if (templateType == "option-set" && field == "name")
        {
            ValidateText(value, "option-set translation name", 120, required: false);
        }

        if (templateType == "ingredient" && field == "name")
        {
            return await SetIngredientTranslationAsync(adoption, locale, value, cancellationToken);
        }

        if (templateType is "item" or "bundle")
        {
            return await SetProductTranslationAsync(adoption, locale, field, value, cancellationToken);
        }

        if (templateType == "option-set" && field == "name")
        {
            return await SetOptionSetTranslationAsync(adoption, locale, value, cancellationToken);
        }

        return false;
    }

    private static string? TranslationValue(
        CentralCatalogueTemplateRevision revision,
        string locale,
        string field)
    {
        if (!revision.Translations.TryGetValue(locale, out var text)) return null;
        return field == "name" ? text.Name : text.Description;
    }

    private async Task<bool> SetIngredientTranslationAsync(
        CatalogueTemplateAdoption adoption,
        string locale,
        string? value,
        CancellationToken cancellationToken)
    {
        var entity = await context.GlobalIngredientTranslations.FirstOrDefaultAsync(
            item => item.GlobalIngredientId == adoption.LocalEntityId && item.LanguageCode == locale,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (entity is null) return false;
            context.GlobalIngredientTranslations.Remove(entity);
            return true;
        }

        if (entity is null)
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

        if (string.Equals(entity.Name, value, StringComparison.Ordinal)) return false;
        entity.Name = value;
        Touch(entity);
        return true;
    }

    private async Task<bool> SetProductTranslationAsync(
        CatalogueTemplateAdoption adoption,
        string locale,
        string field,
        string? value,
        CancellationToken cancellationToken)
    {
        var entity = await context.ProductDescriptions.FirstOrDefaultAsync(
            item => item.ProductId == adoption.LocalEntityId && item.Lang == locale, cancellationToken);
        if (entity is null && value is not null)
        {
            entity = CreateProductDescription(adoption, locale, field);
        }

        if (entity is null) return false;
        if (value is null && field == "name") return RemoveProductTranslationName(entity);
        if (field == "name") return SetProductTranslationName(entity, value!);
        return field == "description" && SetProductTranslationDescription(entity, value);
    }

    private ProductDescription CreateProductDescription(
        CatalogueTemplateAdoption adoption,
        string locale,
        string field)
    {
        if (field == "description")
        {
            throw new ConflictException("Select the matching translation name before adding a translation description.");
        }

        var entity = new ProductDescription
        {
            Id = Guid.NewGuid(),
            ProductId = adoption.LocalEntityId,
            Lang = locale,
            Name = string.Empty,
            Description = string.Empty,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
        context.ProductDescriptions.Add(entity);
        return entity;
    }

    private bool RemoveProductTranslationName(ProductDescription entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Description))
        {
            throw new ConflictException(
                "The source removed this translation name, but the tenant description has local text. Edit or clear it first.");
        }

        context.ProductDescriptions.Remove(entity);
        return true;
    }

    private bool SetProductTranslationName(ProductDescription entity, string value)
    {
        if (string.Equals(entity.Name, value, StringComparison.Ordinal)) return false;
        entity.Name = value;
        Touch(entity);
        return true;
    }

    private bool SetProductTranslationDescription(ProductDescription entity, string? value)
    {
        var description = value ?? string.Empty;
        if (string.Equals(entity.Description, description, StringComparison.Ordinal)) return false;
        entity.Description = description;
        Touch(entity);
        return true;
    }

    private async Task<bool> SetOptionSetTranslationAsync(
        CatalogueTemplateAdoption adoption,
        string locale,
        string? value,
        CancellationToken cancellationToken)
    {
        var entity = await context.OptionSetTranslations.FirstOrDefaultAsync(
            item => item.OptionSetId == adoption.LocalEntityId && item.LanguageCode == locale,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (entity is null) return false;
            context.OptionSetTranslations.Remove(entity);
            return true;
        }

        if (entity is null)
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

        if (string.Equals(entity.Name, value, StringComparison.Ordinal)) return false;
        entity.Name = value;
        Touch(entity);
        return true;
    }
}
