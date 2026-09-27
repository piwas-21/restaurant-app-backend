using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueTemplateImportExecutor
{
    private async Task<ImportedOptionSet> LoadReferencedOptionSetAsync(
        CatalogueImportSession session,
        CatalogueImportEntityResolver resolver,
        CatalogueSourceReference reference,
        CancellationToken cancellationToken)
    {
        var dependency = RequireSessionTemplate(session, reference, "option-set");
        var revision = CatalogueSessionMapper.ParseRevision(dependency.RevisionJson);
        var expectedKind = CatalogueImportOptionSetMapper.ParseKind(
            CatalogueImportPayloadReader.ReadOptionSet(revision).Kind);
        var id = resolver.ResolveDependency(session, reference, "OptionSet");
        var set = await context.OptionSets.AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new ImportedOptionSet(candidate.Id, candidate.Version, candidate.Kind))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BadRequestException("A referenced tenant option set no longer exists.", "LOCAL_DEPENDENCY_MISSING");
        if (set.Kind != expectedKind)
        {
            throw new BadRequestException("The referenced tenant option set has a different kind than its source.",
                "OPTION_SET_KIND_MISMATCH");
        }

        return set;
    }

    private async Task<OptionSetMaterializationTargetResultDto> ApplyImportedSetAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        string sourceTargetKey,
        ImportedOptionSet set,
        OptionSetMaterializationTargetRequest target,
        IReadOnlySet<Guid> stagedProductIds,
        CancellationToken cancellationToken)
    {
        var result = await optionSetMaterializer.ApplyImportedAsync(new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = CatalogueImportOptionSetMapper.StableMaterializationKey(
                session.AdoptionId, revision.TemplateId, revision.Revision, $"apply:{sourceTargetKey}"),
            Targets = [target]
        }, stagedProductIds, cancellationToken);
        var applied = result.Targets.SingleOrDefault()
            ?? throw new BadRequestException("Option-set materialization did not return a target result.",
                "OPTION_SET_MATERIALIZATION_RESULT_MISSING");
        if (applied.Status is not ("applied" or "unchanged"))
        {
            throw new BadRequestException("The tenant rejected an imported option-set attachment.",
                "OPTION_SET_MATERIALIZATION_FAILED");
        }

        return applied;
    }

    private static CatalogueImportSessionTemplate RequireSessionTemplate(
        CatalogueImportSession session,
        CatalogueSourceReference reference,
        string expectedType)
    {
        var dependency = session.Templates.FirstOrDefault(item =>
            item.TemplateId == reference.TemplateId && item.Revision == reference.Revision);
        if (dependency is null || dependency.Type != expectedType)
        {
            throw new BadRequestException(
                $"The required {expectedType} dependency {reference.Key} is not available in this session.",
                "LOCAL_DEPENDENCY_MISSING");
        }

        return dependency;
    }

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

    private async Task<CatalogueTemplateImportOutcome> CreateOptionSetAsync(
        CatalogueImportSession session,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        CatalogueImportEntityResolver resolver,
        CancellationToken cancellationToken)
    {
        var payload = CatalogueImportPayloadReader.ReadOptionSet(revision);
        var request = CatalogueImportOptionSetMapper.ForTemplate(revision, session, payload, decision, resolver);
        var result = await optionSetMaterializer.CreateOrReuseImportedSetAsync(request, cancellationToken);
        if (CatalogueImportTranslationMapper.OwnerMetadata(revision, includeDescription: false) is not null)
        {
            var savedSet = await context.OptionSets.AsNoTracking().Include(candidate => candidate.Translations)
                .SingleAsync(candidate => candidate.Id == result.OptionSetId, cancellationToken);
            await translationProvenance.RecordTemplateAsync(
                CatalogueImportTranslationMapper.ForOptionSet(revision, savedSet), cancellationToken);
        }

        return Imported("OptionSet", result.OptionSetId);
    }

    private Task<int> RecordTemplateTranslationAsync(
        CentralCatalogueTemplateRevision revision,
        RestaurantSystem.Api.Features.TranslationWorkbench.Services.RecordTemplateTranslationRequest request,
        CancellationToken cancellationToken) =>
        CatalogueImportTranslationMapper.OwnerMetadata(revision) is null
            ? Task.FromResult(0)
            : translationProvenance.RecordTemplateAsync(request, cancellationToken);

}
