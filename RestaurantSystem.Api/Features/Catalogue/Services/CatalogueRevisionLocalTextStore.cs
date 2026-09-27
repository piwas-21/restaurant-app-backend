using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed partial class CatalogueRevisionLocalTextStore(ApplicationDbContext context, ICurrentUserService currentUser)
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex TranslationPath = new(
        "^translations\\[([a-zA-Z0-9-]+)\\]\\.(name|description)$", RegexOptions.CultureInvariant,
        RegexMatchTimeout);
    private static readonly Regex SectionPath = new(
        "^sections\\[([a-zA-Z0-9_-]+)\\]\\.(name|translations\\[([a-zA-Z0-9-]+)\\]\\.name)$",
        RegexOptions.CultureInvariant, RegexMatchTimeout);

    public async Task ApplyAsync(
        CatalogueTemplateAdoption adoption,
        string templateType,
        CentralCatalogueTemplateRevision revision,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        IReadOnlyCollection<string> fieldPaths,
        CancellationToken cancellationToken)
    {
        var optionSetChanged = false;
        var changedMenuDefinitions = new HashSet<Guid>();
        foreach (var path in fieldPaths.OrderBy(path => path.EndsWith(".description", StringComparison.Ordinal) ? 1 : 0))
        {
            if (path == "name")
            {
                var changed = await SetCanonicalNameAsync(adoption, templateType, revision.Name, cancellationToken);
                optionSetChanged |= templateType == "option-set" && changed;
                continue;
            }

            if (path == "description")
            {
                await SetCanonicalDescriptionAsync(adoption, templateType, revision.Description, cancellationToken);
                continue;
            }

            var translation = TranslationPath.Match(path);
            if (translation.Success)
            {
                var changed = await SetTranslationAsync(adoption, templateType, translation.Groups[1].Value,
                    translation.Groups[2].Value, revision, cancellationToken);
                optionSetChanged |= templateType == "option-set" && changed;
                continue;
            }

            var section = SectionPath.Match(path);
            if (section.Success)
            {
                var menuDefinitionId = await SetSectionFieldAsync(adoption, entryMappings, section.Groups[1].Value,
                    section.Groups[2].Value, section.Groups[3].Value, revision, cancellationToken);
                if (menuDefinitionId is Guid changedMenuDefinitionId)
                {
                    changedMenuDefinitions.Add(changedMenuDefinitionId);
                }

                continue;
            }

            throw new BadRequestException("Revision field path is invalid.");
        }

        if (optionSetChanged)
        {
            var optionSet = await context.OptionSets.FirstOrDefaultAsync(
                value => value.Id == adoption.LocalEntityId, cancellationToken)
                ?? throw new ConflictException("The mapped tenant option set no longer exists.");
            optionSet.Version++;
            Touch(optionSet);
        }

        foreach (var menuDefinitionId in changedMenuDefinitions)
        {
            var definition = await context.MenuDefinitions.FirstOrDefaultAsync(
                value => value.Id == menuDefinitionId, cancellationToken)
                ?? throw new ConflictException("The mapped bundle menu no longer exists.");
            definition.AuthoringVersion++;
            Touch(definition);
        }
    }

    private async Task<bool> SetCanonicalNameAsync(
        CatalogueTemplateAdoption adoption,
        string templateType,
        string value,
        CancellationToken cancellationToken)
    {
        switch (templateType)
        {
            case "category":
                {
                    var entity = await context.Categories.FirstOrDefaultAsync(x => x.Id == adoption.LocalEntityId, cancellationToken);
                    if (entity is null) throw new ConflictException("The mapped tenant category no longer exists.");
                    return SetText(entity, entity.Name, value, next => entity.Name = next!, "category name", null);
                }
            case "ingredient":
                {
                    var entity = await context.GlobalIngredients.FirstOrDefaultAsync(x => x.Id == adoption.LocalEntityId, cancellationToken);
                    if (entity is null) throw new ConflictException("The mapped tenant ingredient no longer exists.");
                    return SetText(entity, entity.DefaultName, value, next => entity.DefaultName = next!, "ingredient name", null);
                }
            case "item":
            case "bundle":
                {
                    var entity = await context.Products.FirstOrDefaultAsync(x => x.Id == adoption.LocalEntityId, cancellationToken);
                    if (entity is null) throw new ConflictException("The mapped tenant product no longer exists.");
                    return SetText(entity, entity.Name, value, next => entity.Name = next!, "product name", 255);
                }
            case "option-set":
                {
                    var entity = await context.OptionSets.FirstOrDefaultAsync(x => x.Id == adoption.LocalEntityId, cancellationToken);
                    if (entity is null) throw new ConflictException("The mapped tenant option set no longer exists.");
                    ValidateText(value, "option-set name", 120, required: true);
                    if (string.Equals(entity.Name, value, StringComparison.Ordinal)) return false;
                    var normalizedName = OptionSetNameNormalizer.Normalize(value);
                    var duplicate = await context.OptionSets.AnyAsync(candidate => candidate.Id != entity.Id &&
                        candidate.Kind == entity.Kind && candidate.NormalizedName == normalizedName, cancellationToken);
                    if (duplicate)
                    {
                        throw new ConflictException("The updated option-set name conflicts with another set of the same kind.");
                    }

                    entity.Name = value;
                    entity.NormalizedName = normalizedName;
                    Touch(entity);
                    return true;
                }
        }

        return false;
    }

    private async Task SetCanonicalDescriptionAsync(
        CatalogueTemplateAdoption adoption,
        string templateType,
        string? value,
        CancellationToken cancellationToken)
    {
        if (templateType == "category")
        {
            var entity = await context.Categories.FirstOrDefaultAsync(x => x.Id == adoption.LocalEntityId, cancellationToken);
            if (entity is null) throw new ConflictException("The mapped tenant category no longer exists.");
            SetText(entity, entity.Description, value, next => entity.Description = next, "category description", null);
        }
        else if (templateType is "item" or "bundle")
        {
            var entity = await context.Products.FirstOrDefaultAsync(x => x.Id == adoption.LocalEntityId, cancellationToken);
            if (entity is null) throw new ConflictException("The mapped tenant product no longer exists.");
            SetText(entity, entity.Description, value, next => entity.Description = next, "product description", 1000);
        }
    }

}
