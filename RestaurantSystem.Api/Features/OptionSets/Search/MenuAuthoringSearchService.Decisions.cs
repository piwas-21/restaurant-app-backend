using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    public async Task<MenuAuthoringMatchDecisionDto> RecordDecisionAsync(
        MenuAuthoringMatchDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var (normalized, accepted, alias) = await ValidateDecisionAsync(request, cancellationToken);
        var candidateType = request.CandidateType;
        var row = await _context.OptionSetMatchDecisions.FirstOrDefaultAsync(decision =>
            decision.NormalizedName == normalized && decision.CandidateType == candidateType
            && decision.CandidateId == request.CandidateId, cancellationToken);
        var now = DateTime.UtcNow;
        var actor = _currentUser.GetAuditIdentifier();
        if (row is null)
        {
            row = new OptionSetMatchDecision
            {
                NormalizedName = normalized,
                CandidateType = candidateType,
                CandidateId = request.CandidateId,
                IsAccepted = accepted,
                Alias = accepted ? alias : null,
                CreatedAt = now,
                CreatedBy = actor
            };
            await _context.OptionSetMatchDecisions.AddAsync(row, cancellationToken);
        }
        else
        {
            row.IsAccepted = accepted;
            row.Alias = accepted ? alias : null;
            row.UpdatedAt = now;
            row.UpdatedBy = actor;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException("This search decision changed concurrently. Retry the decision.", exception);
        }

        return new MenuAuthoringMatchDecisionDto
        {
            NormalizedName = normalized,
            CandidateType = candidateType,
            CandidateId = request.CandidateId,
            Decision = accepted ? "accept" : "reject",
            Alias = accepted ? alias : null
        };
    }

    private async Task<(string Normalized, bool Accepted, string Alias)> ValidateDecisionAsync(
        MenuAuthoringMatchDecisionRequestDto request, CancellationToken cancellationToken)
    {
        var normalized = ValidateQuery(request.Query);
        var accepted = request.Decision switch
        {
            "accept" => true,
            "reject" => false,
            _ => throw new BadRequestException("Decision must be 'accept' or 'reject'")
        };
        if (!MenuAuthoringCandidateTypes.IsKnown(request.CandidateType) || request.CandidateId == Guid.Empty
            || !await CandidateExistsAsync(request.CandidateType, request.CandidateId, cancellationToken))
        {
            throw new BadRequestException("Choose an active candidate from this tenant's authoring search");
        }

        var alias = string.IsNullOrWhiteSpace(request.Alias) ? request.Query.Trim() : request.Alias.Trim();
        if (accepted && (alias.Length > _pagination.MaximumSearchAliasLength
            || OptionSetNameNormalizer.Normalize(alias) != normalized))
        {
            throw new BadRequestException(
                $"An accepted alias must preserve the normalized search phrase and be at most {_pagination.MaximumSearchAliasLength} characters");
        }

        if (!accepted && !string.IsNullOrWhiteSpace(request.Alias))
        {
            throw new BadRequestException("Rejected candidates cannot carry an alias");
        }

        return (normalized, accepted, alias);
    }

    private async Task<bool> CandidateExistsAsync(string type, Guid id, CancellationToken cancellationToken)
    {
        if (type == MenuAuthoringCandidateTypes.Product)
        {
            return await _context.Products.AnyAsync(product => product.Id == id && product.IsActive
                && product.IsAvailable && !product.IsComponent && product.Type != ProductType.Menu, cancellationToken);
        }

        if (type == MenuAuthoringCandidateTypes.Component)
        {
            return await _context.Products.AnyAsync(product => product.Id == id && product.IsActive
                && product.IsAvailable && product.IsComponent, cancellationToken);
        }

        if (type == MenuAuthoringCandidateTypes.Bundle)
        {
            return await _context.Products.AnyAsync(product => product.Id == id && product.IsActive
                && product.IsAvailable && product.Type == ProductType.Menu, cancellationToken);
        }

        if (type == MenuAuthoringCandidateTypes.Ingredient)
        {
            return await _context.GlobalIngredients.AnyAsync(ingredient => ingredient.Id == id
                && ingredient.IsActive && ingredient.ArchivedAt == null, cancellationToken);
        }

        return await _context.OptionSets.AnyAsync(set => set.Id == id && set.Status == OptionSetStatus.Active,
            cancellationToken);
    }
}
