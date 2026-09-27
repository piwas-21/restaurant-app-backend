using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Categories.Commands.CreateCategoryCommand;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Commands.CreateGlobalIngredientCommand;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueTemplateImportExecutor : ICatalogueTemplateImportExecutor
{
    private readonly ApplicationDbContext context;
    private readonly CustomMediator mediator;
    private readonly IOptionSetMaterializer optionSetMaterializer;
    private readonly ITranslationProvenanceWriter translationProvenance;

    public CatalogueTemplateImportExecutor(
        ApplicationDbContext context,
        CustomMediator mediator,
        IOptionSetMaterializer optionSetMaterializer,
        ITranslationProvenanceWriter translationProvenance)
    {
        this.context = context;
        this.mediator = mediator;
        this.optionSetMaterializer = optionSetMaterializer;
        this.translationProvenance = translationProvenance;
    }

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
        if (!CatalogueImportTranslationMapper.IsReviewed(revision))
        {
            throw new BadRequestException(
                "Only reviewed catalogue template revisions can be imported.", "TEMPLATE_QUALITY_NOT_REVIEWED");
        }

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
            "option-set" => await CreateOptionSetAsync(session, revision, decision, resolver, cancellationToken),
            "bundle" => await CreateBundleAsync(session, revision, decision, resolver, cancellationToken),
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
        await RecordTemplateTranslationAsync(
            revision, CatalogueImportTranslationMapper.ForIngredient(revision, ingredient), cancellationToken);
        return Imported("GlobalIngredient", ingredient.Id);
    }

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

    private sealed record ImportedOptionSet(Guid Id, int Version, OptionSetKind Kind);

    private sealed record ImportedProductChoiceGroup(Guid Id, int AuthoringVersion, int DisplayOrder);
}
