using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed partial class TranslationProvenanceWriter(
    ApplicationDbContext context,
    ICurrentUserService currentUser) : ITranslationProvenanceWriter
{
    private const string NameField = "name";
    private const string DescriptionField = "description";
    private const string ReviewedStatus = "reviewed";

    public async Task<int> RecordTemplateAsync(
        RecordTemplateTranslationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateTemplateRequest(request);
        var previous = await context.TranslationFieldProvenances
            .Where(row => row.EntityType == request.EntityType && row.EntityId == request.EntityId)
            .ToDictionaryAsync(row => (row.FieldKey, row.Locale), cancellationToken);
        var actor = currentUser.GetAuditIdentifier();
        var stored = 0;
        foreach (var value in request.ReviewedValues)
        {
            if (!TryResolveTemplateValue(request, value, out var sourceText, out var actual)) continue;
            previous.TryGetValue((value.FieldKey, value.Locale), out var existing);
            Upsert(existing, new ProvenanceWrite
            {
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                FieldKey = value.FieldKey,
                Locale = value.Locale,
                SourceLocale = request.SourceLocale,
                SourceHash = TranslationWorkbenchRules.Hash($"{request.SourceLocale}\n{sourceText}"),
                TextHash = TranslationWorkbenchRules.Hash(actual),
                Kind = "template",
                ReviewStatus = ReviewedStatus,
                TemplateId = request.TemplateId,
                TemplateRevision = request.TemplateRevision,
                Actor = actor
            });
            stored++;
        }

        return stored;
    }

    private static void ValidateTemplateRequest(RecordTemplateTranslationRequest request)
    {
        if (!IsSupportedEntityType(request.EntityType) || request.EntityId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.TemplateId) || request.TemplateId.Length > 120 ||
            request.TemplateRevision < 1 || !TranslationWorkbenchRules.GuestLocales.Contains(request.SourceLocale))
        {
            throw new BadRequestException("Invalid reviewed template translation evidence");
        }

        if (request.ReviewedValues.Count > 20 ||
            request.ReviewedValues.Any(value => !IsValidReviewedValue(request.EntityType, value)) ||
            request.ReviewedValues.Select(value => (value.FieldKey, value.Locale)).Distinct().Count() !=
                request.ReviewedValues.Count)
        {
            throw new BadRequestException("Invalid reviewed template translation evidence");
        }
    }

    private static bool IsSupportedEntityType(string entityType) => entityType is
        "product" or "globalIngredient" or "productIngredient" or "productVariation" or
        "menuSection" or "optionSet" or "category";

    private static bool IsValidReviewedValue(string entityType, TemplateTranslationEvidence value) =>
        (value.FieldKey is NameField or DescriptionField) &&
        !(entityType == "globalIngredient" && value.FieldKey != NameField) &&
        TranslationWorkbenchRules.GuestLocales.Contains(value.Locale) &&
        !string.IsNullOrWhiteSpace(value.Text) &&
        value.Text.Length <= TranslationWorkbenchRules.MaxLength(value.FieldKey);

    private static bool TryResolveTemplateValue(
        RecordTemplateTranslationRequest request,
        TemplateTranslationEvidence value,
        out string sourceText,
        out string actual)
    {
        sourceText = GetSourceText(request.SavedText, value.FieldKey) ?? string.Empty;
        actual = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceText)) return false;
        var resolved = value.Locale == request.SourceLocale
            ? sourceText
            : GetTranslations(request.SavedText, value.FieldKey).GetValueOrDefault(value.Locale);
        if (resolved is null || !string.Equals(resolved, value.Text, StringComparison.Ordinal)) return false;
        actual = resolved;
        return true;
    }

}
