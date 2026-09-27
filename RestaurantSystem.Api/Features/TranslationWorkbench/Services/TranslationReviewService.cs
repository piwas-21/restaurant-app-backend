using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
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
        ValidateRequest(request);
        var ids = request.Decisions.Select(decision => decision.SuggestionId).ToArray();
        var rows = await context.TranslationSuggestions.Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var actor = currentUser.GetAuditIdentifier();
        var now = DateTime.UtcNow;
        var results = new List<TranslationDecisionResultDto>(request.Decisions.Count);
        foreach (var decision in request.Decisions)
        {
            results.Add(ReviewDecision(decision, rows, actor, now));
        }

        await context.SaveChangesAsync(cancellationToken);
        return new TranslationReviewDto(results);
    }

    private static void ValidateRequest(TranslationReviewRequestDto request)
    {
        if (request.Decisions is null or { Count: < 1 or > 40 } ||
            request.Decisions.Any(IsInvalidDecision) ||
            request.Decisions.Select(decision => decision.SuggestionId).Distinct().Count() != request.Decisions.Count)
        {
            throw new BadRequestException("Invalid translation review decisions");
        }
    }

    private static bool IsInvalidDecision(TranslationDecisionDto? decision) =>
        decision is null || decision.SuggestionId == Guid.Empty ||
        decision.Decision is not ("accept" or "edit" or "reject") ||
        decision.Decision == "edit" &&
            (string.IsNullOrWhiteSpace(decision.Text) || decision.Text.Length > 1000);

    private static TranslationDecisionResultDto ReviewDecision(
        TranslationDecisionDto decision,
        Dictionary<Guid, TranslationSuggestion> rows,
        string actor,
        DateTime now)
    {
        if (!rows.TryGetValue(decision.SuggestionId, out var row) ||
            row.ClientKey is not null && row.RequestedBy != actor)
        {
            return new TranslationDecisionResultDto(decision.SuggestionId,
                decision.Decision, "notFound", null);
        }

        if (row.Status is "rejected" or "stale")
        {
            return new TranslationDecisionResultDto(decision.SuggestionId,
                decision.Decision, "stale", null);
        }

        var text = ReviewedText(decision, row);
        if (text is not null && text.Length > TranslationWorkbenchRules.MaxLength(row.FieldKey))
        {
            throw new BadRequestException("Reviewed translation is too long");
        }

        row.Status = ReviewedStatus(decision.Decision);
        row.ReviewedText = text;
        row.ReviewerId = actor;
        row.ReviewedAt = now;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;
        return new TranslationDecisionResultDto(row.Id, decision.Decision, row.Status, text);
    }

    private static string? ReviewedText(TranslationDecisionDto decision, TranslationSuggestion row) =>
        decision.Decision switch
        {
            "accept" => row.SuggestedText,
            "edit" => decision.Text,
            _ => null
        };

    private static string ReviewedStatus(string decision) => decision switch
    {
        "accept" => "accepted",
        "edit" => "edited",
        _ => "rejected"
    };
}
