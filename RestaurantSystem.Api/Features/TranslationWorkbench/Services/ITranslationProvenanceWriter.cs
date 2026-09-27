using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public interface ITranslationProvenanceWriter
{
    Task RecordAsync(
        string entityType,
        Guid entityId,
        TranslationOwnerMetadataDto? metadata,
        TranslationTextMap text,
        CancellationToken cancellationToken);

    Task<int> RecordTemplateAsync(
        string entityType,
        Guid entityId,
        string templateId,
        int templateRevision,
        string sourceLocale,
        TranslationTextMap savedText,
        IReadOnlyList<TemplateTranslationEvidence> reviewedValues,
        CancellationToken cancellationToken);
}
