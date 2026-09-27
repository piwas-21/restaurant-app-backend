using System.Text.Json;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.CreateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueTemplateImportExecutor(
    CustomMediator mediator) : ICatalogueTemplateImportExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = { new StringEnumConverterFactory() }
    };

    public async Task<CatalogueTemplateImportOutcome> ExecuteAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        CatalogueImportBatchContext batchContext,
        CancellationToken cancellationToken)
    {
        batchContext.EnsureFresh();
        var revision = CatalogueSessionMapper.ParseRevision(item.RevisionJson);
        CatalogueImportPayloadReader.EnsureSupportedPayload(revision);
        if (revision.Type == "cuisine-pack")
        {
            return Imported(null, null);
        }

        var entityType = CatalogueImportReviewRules.ExpectedEntityType(item.Type);
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw CatalogueImportPayloadReader.Unsupported("This template type cannot be imported into the tenant catalogue.");
        }

        var resolver = new CatalogueImportEntityResolver(batchContext);
        var mapping = resolver.FindReusableMapping(item.TemplateId, item.Revision, entityType);
        if (mapping is not null)
        {
            return Imported(mapping.LocalEntityType, mapping.LocalEntityId);
        }

        var decision = ReadDecision(item.DecisionJson)
            ?? throw new BadRequestException("Resolve this template before importing it.", "RESOLUTION_REQUIRED");
        if (decision.Resolution.Equals("Reuse", StringComparison.OrdinalIgnoreCase))
        {
            var id = decision.LocalEntityId ?? throw new BadRequestException("Reuse needs a tenant record ID.");
            if (!resolver.LocalEntityExists(entityType, id))
            {
                throw new BadRequestException("The selected tenant record no longer exists.", "REUSE_TARGET_MISSING");
            }

            return Imported(entityType, id);
        }

        return item.Type switch
        {
            "category" => await CreateCategoryAsync(session, revision, decision, cancellationToken),
            "ingredient" => await CreateIngredientAsync(session, revision, decision, cancellationToken),
            "item" => await CreateItemAsync(session, revision, decision, resolver, cancellationToken),
            "option-set" or "bundle" => throw UnsupportedUntilMaterialization(item.Type),
            _ => throw CatalogueImportPayloadReader.Unsupported("This template type is not supported by the tenant importer.")
        };
    }

    private async Task<CatalogueTemplateImportOutcome> CreateCategoryAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        CancellationToken cancellationToken)
    {
        var command = CatalogueImportCommandMapper.Category(revision, session.Locale, decision);
        var response = await mediator.SendCommand<ApiResponse<CategoryDto>>(command, cancellationToken);
        var category = RequireData(response, "Category creation was rejected by tenant validation.");
        return Imported("Category", category.Id);
    }

    private async Task<CatalogueTemplateImportOutcome> CreateIngredientAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        CancellationToken cancellationToken)
    {
        var command = CatalogueImportCommandMapper.Ingredient(revision, session.Locale, decision);
        var response = await mediator.SendCommand<ApiResponse<GlobalIngredientDto>>(command, cancellationToken);
        var ingredient = RequireData(response, "Ingredient creation was rejected by tenant validation.");
        return Imported("GlobalIngredient", ingredient.Id);
    }

    private async Task<CatalogueTemplateImportOutcome> CreateItemAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver,
        CancellationToken cancellationToken)
    {
        var choiceSets = CatalogueImportPayloadReader.ReadReferences(revision.Payload, "optionSets");
        var sideSets = CatalogueImportPayloadReader.ReadReferences(revision.Payload, "sideSets");
        if (choiceSets.Any(reference => IsSelected(session, reference)) ||
            sideSets.Any(reference => IsSelected(session, reference)))
        {
            throw UnsupportedUntilMaterialization("item choice sets");
        }

        var category = CatalogueImportPayloadReader.ReadOptionalReference(revision.Payload, "category")
            ?? throw new BadRequestException(
                "This item has no reviewed category reference. Assign a category before importing it.",
                "ITEM_CATEGORY_REQUIRED");
        var categoryId = resolver.ResolveDependency(session, category, "Category");
        var command = CatalogueImportCommandMapper.Item(revision, session.Locale, decision, categoryId);
        var response = await mediator.SendCommand<ApiResponse<ProductDto>>(command, cancellationToken);
        var product = RequireData(response, "Item creation was rejected by tenant validation.");
        return Imported("Product", product.Id);
    }

    private static bool IsSelected(CatalogueImportSession session, CatalogueSourceReference reference) =>
        session.Templates.Any(item => item.TemplateId == reference.TemplateId &&
            item.Revision == reference.Revision && item.IsSelected);

    private static CatalogueImportItemDecision? ReadDecision(string? json) => string.IsNullOrWhiteSpace(json)
        ? null
        : JsonSerializer.Deserialize<CatalogueImportItemDecision>(json, JsonOptions);

    private static T RequireData<T>(ApiResponse<T> response, string failureMessage)
    {
        if (!response.Success || response.Data is null)
        {
            var detail = response.Errors?.FirstOrDefault();
            throw new BadRequestException(string.IsNullOrWhiteSpace(detail) ? failureMessage : detail);
        }

        return response.Data;
    }

    private static CatalogueTemplateImportOutcome Imported(string? entityType, Guid? entityId) =>
        new(CatalogueImportItemStatus.Imported, entityType, entityId, []);

    private static BadRequestException UnsupportedUntilMaterialization(string type) => new(
        $"Importing {type} requires the shared option-set materialization path, which is not available for this tenant yet.",
        "OPTION_SET_MATERIALIZATION_UNAVAILABLE");
}
