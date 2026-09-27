using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueTemplateImportExecutor
{
    private async Task<CatalogueTemplateImportOutcome> CreateBundleAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver,
        CancellationToken cancellationToken)
    {
        var sections = CatalogueImportPayloadReader.ReadBundleSections(revision);
        var sectionDtos = sections.Select(section => MapBundleSection(revision, session, section, decision, resolver)).ToList();
        var parentReference = CatalogueImportPayloadReader.ReadOptionalReference(revision.Payload, "standaloneOffer");
        Guid? parentOfferId = parentReference is null
            ? null
            : resolver.ResolveDependency(session, parentReference, "Product");
        var command = CatalogueImportCommandMapper.Bundle(
            revision, session.Locale, decision, sectionDtos, parentOfferId);
        var response = await mediator.SendCommand<ApiResponse<ProductDto>>(command, cancellationToken);
        var bundle = RequireData(response, "Bundle creation was rejected by tenant validation.");
        var menu = bundle.MenuDefinition
            ?? throw new BadRequestException("Bundle creation did not return its menu definition.", "MENU_DEFINITION_MISSING");
        await RecordTemplateTranslationAsync(
            revision, CatalogueImportTranslationMapper.ForProduct(revision, bundle), cancellationToken);
        foreach (var section in sections)
        {
            var savedSection = menu.Sections.SingleOrDefault(candidate =>
                candidate.DisplayOrder == section.DisplayOrder && candidate.Name == section.Name)
                ?? throw new BadRequestException("Bundle creation did not return the reviewed section text.",
                    "MENU_SECTION_MISSING");
            await RecordTemplateTranslationAsync(
                revision, CatalogueImportTranslationMapper.ForSection(revision, section, savedSection), cancellationToken);
        }
        var stagedProducts = CatalogueImportOptionSetMapper.StagedProductIds(session, bundle.Id);
        var menuVersion = menu.AuthoringVersion;

        foreach (var section in sections)
        {
            var storedSection = menu.Sections.SingleOrDefault(candidate => candidate.DisplayOrder == section.DisplayOrder)
                ?? throw new BadRequestException("Bundle creation did not persist a reviewed choice section.",
                    "MENU_SECTION_MISSING");
            var setRequest = CatalogueImportOptionSetMapper.ForBundleSection(
                revision, session, section, decision, resolver);
            setRequest.StagedProductIds = stagedProducts;
            var importedSet = await optionSetMaterializer.CreateOrReuseImportedSetAsync(setRequest, cancellationToken);
            if (CatalogueImportTranslationMapper.OwnerMetadata(revision, includeDescription: false) is not null)
            {
                var savedSet = await context.OptionSets.AsNoTracking().Include(candidate => candidate.Translations)
                    .SingleAsync(candidate => candidate.Id == importedSet.OptionSetId, cancellationToken);
                await RecordTemplateTranslationAsync(revision,
                    CatalogueImportTranslationMapper.ForBundleSectionOptionSet(revision, section, savedSet),
                    cancellationToken);
            }

            var targetKey = CatalogueImportOptionSetMapper.StableMaterializationKey(
                session.AdoptionId, revision.TemplateId, revision.Revision, section.Key);
            var target = new OptionSetMaterializationTargetRequest
            {
                TargetKey = targetKey,
                Role = OptionSetAttachmentRole.BundleChoice,
                TargetProductId = bundle.Id,
                TargetMenuSectionId = storedSection.Id,
                ExpectedMenuAuthoringVersion = menuVersion,
                Settings = CatalogueImportOptionSetMapper.AttachmentSettings(
                    OptionSetKind.BundleChoice, section.Minimum, section.Maximum, section.DisplayOrder)
            };
            var result = await ApplyImportedSetAsync(session, revision.TemplateId, revision.Revision,
                section.Key, new ImportedOptionSet(importedSet.OptionSetId, importedSet.Version, OptionSetKind.BundleChoice),
                target, stagedProducts, cancellationToken);
            menuVersion = result.MenuAuthoringVersion
                ?? throw new BadRequestException("Bundle choice materialization did not return its updated menu version.",
                    "MENU_AUTHORING_VERSION_MISSING");
        }

        return Imported("MenuBundle", bundle.Id);
    }

    private static MenuSectionDto MapBundleSection(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportSession session,
        CatalogueBundleSection section,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver)
    {
        var items = section.Options.Select(option => new MenuSectionItemDto
        {
            ProductId = resolver.ResolveDependency(session, option.Reference, "Product"),
            AdditionalPrice = RequiredLocalPrice(decision, option.Reference),
            DisplayOrder = option.SortOrder,
            IsDefault = option.IsDefault
        }).ToList();
        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count)
        {
            throw new BadRequestException(
                $"The tenant mappings make section '{section.Name}' contain the same product more than once.",
                "DUPLICATE_LOCAL_BUNDLE_OPTION");
        }

        return new MenuSectionDto
        {
            Name = section.Name,
            DisplayOrder = section.DisplayOrder,
            IsRequired = section.Minimum > 0,
            MinSelection = section.Minimum,
            MaxSelection = section.Maximum,
            Items = items,
            Translations = section.Translations.ToDictionary(
                pair => pair.Key,
                pair => new MenuSectionTranslationDto { Name = pair.Value },
                StringComparer.OrdinalIgnoreCase),
            TranslationMetadata = CatalogueImportTranslationMapper.OwnerMetadata(revision, includeDescription: false)
        };
    }

}
