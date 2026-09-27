using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class TranslationReviewService(
    ApplicationDbContext context,
    ICurrentUserService currentUser) : ITranslationReviewService
{
    public async Task<TranslationReviewDto> ReviewAsync(
        TranslationReviewRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.Decisions is null or { Count: < 1 or > 40 } ||
            request.Decisions.Any(decision => decision is null) ||
            request.Decisions.Select(decision => decision.SuggestionId).Distinct().Count() != request.Decisions.Count ||
            request.Decisions.Any(decision => decision.SuggestionId == Guid.Empty ||
                decision.Decision is not ("accept" or "edit" or "reject") ||
                decision.Decision == "edit" && (string.IsNullOrWhiteSpace(decision.Text) || decision.Text.Length > 1000)))
        {
            throw new BadRequestException("Invalid translation review decisions");
        }

        var ids = request.Decisions.Select(decision => decision.SuggestionId).ToArray();
        var rows = await context.TranslationSuggestions.Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var actor = currentUser.GetAuditIdentifier();
        var now = DateTime.UtcNow;
        var results = new List<TranslationDecisionResultDto>(request.Decisions.Count);
        foreach (var decision in request.Decisions)
        {
            if (!rows.TryGetValue(decision.SuggestionId, out var row) ||
                row.ClientKey is not null && row.RequestedBy != actor)
            {
                results.Add(new TranslationDecisionResultDto(decision.SuggestionId,
                    decision.Decision, "notFound", null));
                continue;
            }

            if (row.Status is "rejected" or "stale")
            {
                results.Add(new TranslationDecisionResultDto(decision.SuggestionId,
                    decision.Decision, "stale", null));
                continue;
            }

            var text = decision.Decision switch
            {
                "accept" => row.SuggestedText,
                "edit" => decision.Text,
                _ => null
            };
            if (text is not null && text.Length > TranslationWorkbenchRules.MaxLength(row.FieldKey))
            {
                throw new BadRequestException("Reviewed translation is too long");
            }

            row.Status = decision.Decision switch
            {
                "accept" => "accepted",
                "edit" => "edited",
                _ => "rejected"
            };
            row.ReviewedText = text;
            row.ReviewerId = actor;
            row.ReviewedAt = now;
            row.UpdatedAt = now;
            row.UpdatedBy = actor;
            results.Add(new TranslationDecisionResultDto(row.Id, decision.Decision, row.Status, text));
        }

        await context.SaveChangesAsync(cancellationToken);
        return new TranslationReviewDto(results);
    }
}
