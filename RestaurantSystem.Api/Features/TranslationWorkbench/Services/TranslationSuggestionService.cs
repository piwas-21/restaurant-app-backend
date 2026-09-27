using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed class TranslationSuggestionService(
    ApplicationDbContext context,
    ITranslationPreviewService preview,
    ITranslationGenerationProvider provider,
    ICurrentUserService currentUser,
    IOptions<TranslationAssistanceSettings> options,
    ILogger<TranslationSuggestionService> logger) : ITranslationSuggestionService
{
    private static readonly SemaphoreSlim GenerationGate = new(1, 1);

    public async Task<TranslationSuggestionsDto> SuggestAsync(
        TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var previewResult = await preview.PreviewAsync(request, cancellationToken);
        var glossary = settings.Glossary;
        var candidates = SelectCandidates(request, previewResult, settings, glossary,
            currentUser.GetAuditIdentifier());
        var skipped = new List<TranslationGapDto>();
        var suggestions = new List<TranslationSuggestionDto>();
        if (candidates.Count == 0)
        {
            return new TranslationSuggestionsDto(suggestions, skipped, ProviderStatus(settings));
        }

        await GenerationGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            // The database OID gives each tenant database a separate lock key. Replicas sharing
            // that database serialize cache and budget checks across provider calls.
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock((SELECT oid::bigint FROM pg_database WHERE datname = current_database()))",
                cancellationToken);
            var uncached = await ReadCacheAsync(candidates, suggestions, skipped, cancellationToken);
            if (uncached.Count == 0)
            {
                return new TranslationSuggestionsDto(suggestions, skipped, ProviderStatus(settings));
            }

            if (ProviderStatus(settings) == "disabled")
            {
                skipped.AddRange(uncached.Select(candidate =>
                    new TranslationGapDto(candidate.FieldRef, candidate.Locale, "providerDisabled")));
                return new TranslationSuggestionsDto(suggestions, skipped, "disabled");
            }

            if (uncached.Count > settings.MaxBatchTargets)
            {
                throw new BadRequestException("Too many translation gaps in one provider batch");
            }

            var targets = uncached.Select((candidate, index) => new TranslationGenerationTarget(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                candidate.SourceLocale,
                candidate.Locale,
                candidate.FieldRef.FieldKey,
                candidate.SourceText,
                candidate.Context?.DishName,
                candidate.Context?.Category,
                candidate.Context?.Exclusions ?? [])).ToArray();
            await EnsureBudgetAsync(settings, targets, glossary, cancellationToken);
            var generated = await provider.GenerateAsync(targets, glossary, cancellationToken);
            var actor = currentUser.GetAuditIdentifier();
            var now = DateTime.UtcNow;
            var batch = new TranslationGenerationBatch
            {
                Id = Guid.NewGuid(),
                Fingerprint = TranslationWorkbenchRules.Hash(string.Join("|", uncached.Select(row => row.Fingerprint))),
                RequestedBy = actor,
                Provider = generated.Provider,
                Model = generated.Model,
                InputTokens = generated.InputTokens,
                OutputTokens = generated.OutputTokens,
                EstimatedCostUsd = EstimateCost(generated, settings),
                CreatedAt = now,
                CreatedBy = actor
            };
            context.TranslationGenerationBatches.Add(batch);
            foreach (var (candidate, index) in uncached.Select((value, index) => (value, index)))
            {
                var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!generated.Texts.TryGetValue(key, out var text) ||
                    !TranslationWorkbenchRules.IsSafeSuggestion(candidate.SourceText, text,
                        candidate.FieldRef.FieldKey))
                {
                    throw new HttpRequestException("Translation provider returned invalid text");
                }

                var row = NewSuggestion(candidate, text, generated, batch.Id, actor, now);
                context.TranslationSuggestions.Add(row);
                suggestions.Add(ToDto(row, candidate.FieldRef));
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Translation batch {BatchId} created {TargetCount} suggestions; input tokens {InputTokens}, output tokens {OutputTokens}",
                batch.Id, uncached.Count, generated.InputTokens, generated.OutputTokens);
            return new TranslationSuggestionsDto(suggestions, skipped, "ready");
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    private static List<TranslationSuggestionCandidate> SelectCandidates(
        TranslationWorkbenchRequestDto request,
        TranslationPreviewDto preview,
        TranslationAssistanceSettings settings,
        IReadOnlyDictionary<string, string> glossary,
        string actor)
    {
        var result = new List<TranslationSuggestionCandidate>();
        for (var index = 0; index < request.Fields.Count; index++)
        {
            var field = request.Fields[index];
            var row = preview.Rows[index];
            var sourceTarget = row.Targets.FirstOrDefault(target => target.Locale == field.SourceLocale);
            var saveEligible = field.FieldRef.ClientKey is not null ||
                sourceTarget?.Provenance?.Kind == "template" ||
                sourceTarget?.Provenance?.SourceHash is { } priorHash && priorHash != row.SourceHash;
            var contextHash = TranslationWorkbenchRules.Hash(JsonSerializer.Serialize(new
            {
                field.Context,
                glossary,
                settings.PromptVersion,
                settings.Model
            }));
            foreach (var target in row.Targets)
            {
                if (target.Locale == field.SourceLocale || target.Status is not ("missing" or "stale" or "sourceCopy") ||
                    request.GenerationIntent == "saveReview" && !saveEligible ||
                    target.Status == "sourceCopy" && request.GenerationIntent != "explicitFill")
                {
                    continue;
                }

                var identity = TranslationWorkbenchRules.Identity(field.FieldRef);
                var actorScope = field.FieldRef.ClientKey is null ? string.Empty : actor;
                var fingerprint = TranslationWorkbenchRules.Hash(
                    $"{identity}|{actorScope}|{target.Locale}|{row.SourceHash}|{contextHash}");
                result.Add(new TranslationSuggestionCandidate(field.FieldRef,
                    field.SourceLocale, target.Locale, field.SourceText,
                    row.SourceHash, field.Context, contextHash, fingerprint));
            }
        }

        return result;
    }

    private async Task<List<TranslationSuggestionCandidate>> ReadCacheAsync(
        IReadOnlyList<TranslationSuggestionCandidate> candidates,
        List<TranslationSuggestionDto> suggestions,
        List<TranslationGapDto> skipped,
        CancellationToken cancellationToken)
    {
        var fingerprints = candidates.Select(candidate => candidate.Fingerprint).Distinct().ToArray();
        var cached = await context.TranslationSuggestions.AsNoTracking()
            .Where(row => fingerprints.Contains(row.Fingerprint))
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);
        var actor = currentUser.GetAuditIdentifier();
        var missing = new List<TranslationSuggestionCandidate>();
        foreach (var candidate in candidates)
        {
            var existing = cached.FirstOrDefault(row => row.Fingerprint == candidate.Fingerprint &&
                (candidate.FieldRef.EntityId.HasValue || row.RequestedBy == actor));
            if (existing?.Status == "rejected")
            {
                skipped.Add(new TranslationGapDto(candidate.FieldRef, candidate.Locale, "previouslyRejected"));
            }
            else if (existing is not null)
            {
                suggestions.Add(ToDto(existing, candidate.FieldRef));
            }
            else
            {
                missing.Add(candidate);
            }
        }

        return missing;
    }

    private async Task EnsureBudgetAsync(
        TranslationAssistanceSettings settings,
        IReadOnlyList<TranslationGenerationTarget> targets,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var usage = await context.TranslationGenerationBatches.AsNoTracking()
            .Where(batch => batch.CreatedAt >= today)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Tokens = group.Sum(batch => batch.InputTokens + batch.OutputTokens),
                Spend = group.Sum(batch => batch.EstimatedCostUsd)
            }).FirstOrDefaultAsync(cancellationToken);
        var inputBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { glossary, targets }));
        var reservedInputTokens = inputBytes + 4096;
        var reservedTokens = reservedInputTokens + settings.MaxOutputTokens;
        var reservedSpend = (reservedInputTokens * settings.InputCostPerMillionUsd +
            settings.MaxOutputTokens * settings.OutputCostPerMillionUsd) / 1_000_000m;
        if ((usage?.Count ?? 0) >= settings.MaxDailyBatches ||
            (usage?.Tokens ?? 0) + reservedTokens > settings.MaxDailyTokens ||
            (usage?.Spend ?? 0m) + reservedSpend > settings.MaxDailySpendUsd)
        {
            throw new BadRequestException("Translation assistance daily limit reached");
        }
    }

    private static TranslationSuggestion NewSuggestion(
        TranslationSuggestionCandidate candidate,
        string text,
        TranslationGenerationResult generated,
        Guid batchId,
        string actor,
        DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            EntityType = candidate.FieldRef.EntityType,
            EntityId = candidate.FieldRef.EntityId,
            ClientKey = candidate.FieldRef.ClientKey,
            FieldKey = candidate.FieldRef.FieldKey,
            Locale = candidate.Locale,
            SourceLocale = candidate.SourceLocale,
            SourceHash = candidate.SourceHash,
            ContextHash = candidate.ContextHash,
            Fingerprint = candidate.Fingerprint,
            SuggestedText = text,
            Provider = generated.Provider,
            Model = generated.Model,
            RequestedBy = actor,
            CreatedAt = now,
            CreatedBy = actor
        };

    private static TranslationSuggestionDto ToDto(
        TranslationSuggestion row,
        TranslationFieldRefDto fieldRef) => new(row.Id, fieldRef,
        row.Locale, row.SourceHash, row.SuggestedText, row.Provider, row.Model, "suggested");

    private static string ProviderStatus(TranslationAssistanceSettings settings) =>
        settings.Enabled && settings.TenantDataApproved &&
        settings.Provider == "openai" &&
        !string.IsNullOrWhiteSpace(settings.ApiKey) &&
        Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? "ready" : "disabled";

    private static decimal EstimateCost(
        TranslationGenerationResult result,
        TranslationAssistanceSettings settings) =>
        (result.InputTokens * settings.InputCostPerMillionUsd +
         result.OutputTokens * settings.OutputCostPerMillionUsd) / 1_000_000m;
}
