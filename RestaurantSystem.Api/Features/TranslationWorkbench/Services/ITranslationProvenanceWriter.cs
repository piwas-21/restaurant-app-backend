using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public sealed record RecordTemplateTranslationRequest(
    string EntityType,
    Guid EntityId,
    string TemplateId,
    int TemplateRevision,
    string SourceLocale,
    TranslationTextMap SavedText,
    IReadOnlyList<TemplateTranslationEvidence> ReviewedValues);

public interface ITranslationProvenanceWriter
{
    Task RecordAsync(
        string entityType,
        Guid entityId,
        TranslationOwnerMetadataDto? metadata,
        TranslationTextMap text,
        CancellationToken cancellationToken);

    Task<int> RecordTemplateAsync(
        RecordTemplateTranslationRequest request,
        CancellationToken cancellationToken);
}
