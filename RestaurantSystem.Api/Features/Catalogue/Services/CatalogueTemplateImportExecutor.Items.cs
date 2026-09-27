using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueTemplateImportExecutor
{
    private async Task<CatalogueTemplateImportOutcome> CreateItemAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver,
        CancellationToken cancellationToken)
    {
        var category = CatalogueImportPayloadReader.ReadOptionalReference(revision.Payload, "category")
            ?? throw new BadRequestException(
                "This item has no reviewed category reference. Assign a category before importing it.",
                "ITEM_CATEGORY_REQUIRED");
        var categoryId = resolver.ResolveDependency(session, category, "Category");
        var optionSetReferences = CatalogueImportPayloadReader.ReadReferences(revision.Payload, "optionSets");
        var sideSetReferences = CatalogueImportPayloadReader.ReadReferences(revision.Payload, "sideSets");
        var groups = BuildProductChoiceGroups(session, optionSetReferences);
        var command = CatalogueImportCommandMapper.Item(revision, session.Locale, decision, categoryId,
            groups.Count == 0 ? null : groups);
        var response = await mediator.SendCommand<ApiResponse<ProductDto>>(command, cancellationToken);
        var product = RequireData(response, "Item creation was rejected by tenant validation.");
        await RecordTemplateTranslationAsync(
            revision, CatalogueImportTranslationMapper.ForProduct(revision, product), cancellationToken);
        await AttachItemOptionSetsAsync(session, revision, resolver, product, optionSetReferences,
            sideSetReferences, cancellationToken);
        return Imported("Product", product.Id);
    }

    private static List<ProductCustomizationGroupDto> BuildProductChoiceGroups(
        CatalogueImportSession session,
        IReadOnlyList<CatalogueSourceReference> optionSetReferences)
    {
        var groups = new List<ProductCustomizationGroupDto>();
        foreach (var reference in optionSetReferences)
        {
            var dependency = RequireSessionTemplate(session, reference, "option-set");
            var dependencyRevision = CatalogueSessionMapper.ParseRevision(dependency.RevisionJson);
            var payload = CatalogueImportPayloadReader.ReadOptionSet(dependencyRevision);
            if (payload.Kind != "bundle-option")
            {
                continue;
            }

            var decision = ReadDecision(dependency.DecisionJson);
            groups.Add(CatalogueImportOptionSetMapper.ProductChoiceGroup(
                dependencyRevision, session.Locale, groups.Count, decision));
        }

        return groups;
    }

    private async Task AttachItemOptionSetsAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportEntityResolver resolver,
        ProductDto product,
        IReadOnlyList<CatalogueSourceReference> optionSetReferences,
        IReadOnlyList<CatalogueSourceReference> sideSetReferences,
        CancellationToken cancellationToken)
    {
        var productChoiceGroups = await context.ProductCustomizationGroups.AsNoTracking()
            .Where(group => group.ProductId == product.Id)
            .OrderBy(group => group.DisplayOrder)
            .Select(group => new ImportedProductChoiceGroup(
                group.Id, group.AuthoringVersion, group.DisplayOrder))
            .ToListAsync(cancellationToken);
        var stagedProducts = CatalogueImportOptionSetMapper.StagedProductIds(session, product.Id);
        var choiceGroupIndex = 0;

        for (var index = 0; index < optionSetReferences.Count; index++)
        {
            var reference = optionSetReferences[index];
            var set = await LoadReferencedOptionSetAsync(session, resolver, reference, cancellationToken);
            var productChoice = set.Kind == OptionSetKind.BundleChoice;
            ImportedProductChoiceGroup? group = null;
            if (productChoice)
            {
                if (choiceGroupIndex >= productChoiceGroups.Count)
                {
                    throw new BadRequestException("A product-choice group was not created for its option set.",
                        "PRODUCT_CHOICE_GROUP_MISSING");
                }

                group = productChoiceGroups[choiceGroupIndex++];
            }

            var role = CatalogueImportOptionSetMapper.AttachmentRole(set.Kind, productChoice);
            var payload = CatalogueImportPayloadReader.ReadOptionSet(
                CatalogueSessionMapper.ParseRevision(RequireSessionTemplate(session, reference, "option-set").RevisionJson));
            var targetKey = CatalogueImportOptionSetMapper.StableMaterializationKey(
                session.AdoptionId, revision.TemplateId, revision.Revision, reference.Key);
            var target = new OptionSetMaterializationTargetRequest
            {
                TargetKey = targetKey,
                Role = role,
                TargetProductId = product.Id,
                TargetCustomizationGroupId = group?.Id,
                ExpectedCustomizationGroupVersion = group?.AuthoringVersion,
                Settings = CatalogueImportOptionSetMapper.AttachmentSettings(
                    set.Kind, payload.Minimum, payload.Maximum, group?.DisplayOrder ?? index)
            };
            await ApplyImportedSetAsync(session, revision, reference.Key,
                set, target, stagedProducts, cancellationToken);
        }
        for (var index = 0; index < sideSetReferences.Count; index++)
        {
            var reference = sideSetReferences[index];
            var set = await LoadReferencedOptionSetAsync(session, resolver, reference, cancellationToken);
            if (set.Kind != OptionSetKind.SuggestedSide)
            {
                throw new BadRequestException("A side-set reference must resolve to a suggested-side option set.",
                    "OPTION_SET_KIND_MISMATCH");
            }

            var targetKey = CatalogueImportOptionSetMapper.StableMaterializationKey(
                session.AdoptionId, revision.TemplateId, revision.Revision, $"side:{reference.Key}");
            var target = new OptionSetMaterializationTargetRequest
            {
                TargetKey = targetKey,
                Role = OptionSetAttachmentRole.SuggestedSide,
                TargetProductId = product.Id,
                Settings = CatalogueImportOptionSetMapper.AttachmentSettings(set.Kind, 0, 0, index)
            };
            await ApplyImportedSetAsync(session, revision,
                $"side:{reference.Key}", set, target, stagedProducts, cancellationToken);
        }
    }

}
