using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueRejectedCandidateService(
    ApplicationDbContext context,
    ICurrentUserService currentUser) : ICatalogueRejectedCandidateService
{
    public async Task SyncAsync(
        CatalogueImportSessionTemplate item,
        string locale,
        CatalogueImportItemDecision decision,
        CancellationToken cancellationToken)
    {
        var candidateType = CandidateType(item.Type);
        var desired = decision.RejectedCandidateIds?.ToHashSet() ?? [];
        if (desired.Count > 0 && (candidateType.Length == 0 || desired.Contains(decision.LocalEntityId ?? Guid.Empty)))
        {
            throw new BadRequestException("A chosen reuse target cannot also be rejected");
        }

        var existing = await context.CatalogueMatchDecisions
            .Where(match => match.SourceTemplateId == item.TemplateId && match.SourceRevision == item.Revision &&
                match.CandidateType == candidateType)
            .ToListAsync(cancellationToken);
        context.CatalogueMatchDecisions.RemoveRange(existing.Where(match => !desired.Contains(match.CandidateId)));
        var missing = desired.Except(existing.Select(match => match.CandidateId)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        if (await CountExistingCandidatesAsync(candidateType, missing, cancellationToken) != missing.Length)
        {
            throw new BadRequestException("A rejected name-match candidate does not exist");
        }

        var revision = CatalogueSessionMapper.ParseRevision(item.RevisionJson);
        var normalizedName = CatalogueSessionMapper.Localized(revision, locale).Name.Trim().ToLowerInvariant();
        context.CatalogueMatchDecisions.AddRange(missing.Select(candidateId => new CatalogueMatchDecision
        {
            SourceTemplateId = item.TemplateId,
            SourceRevision = item.Revision,
            NormalizedName = normalizedName,
            CandidateType = candidateType,
            CandidateId = candidateId,
            Decision = CatalogueMatchDecisionStatus.Rejected,
            CreatedBy = currentUser.GetAuditIdentifier()
        }));
    }

    private Task<int> CountExistingCandidatesAsync(
        string candidateType,
        IReadOnlyCollection<Guid> candidateIds,
        CancellationToken cancellationToken) => candidateType switch
        {
            "Category" => context.Categories.CountAsync(value => candidateIds.Contains(value.Id), cancellationToken),
            "GlobalIngredient" => context.GlobalIngredients.CountAsync(value => candidateIds.Contains(value.Id), cancellationToken),
            "Product" => context.Products.CountAsync(value => candidateIds.Contains(value.Id) && value.Type != ProductType.Menu, cancellationToken),
            "MenuBundle" => context.Products.CountAsync(value => candidateIds.Contains(value.Id) && value.Type == ProductType.Menu, cancellationToken),
            _ => Task.FromResult(0)
        };

    private static string CandidateType(string templateType) => templateType switch
    {
        "category" => "Category",
        "ingredient" => "GlobalIngredient",
        "bundle" => "MenuBundle",
        "item" => "Product",
        _ => string.Empty
    };
}
