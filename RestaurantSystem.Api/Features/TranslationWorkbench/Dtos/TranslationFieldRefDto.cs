namespace RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;

public sealed record TranslationFieldRefDto(
    string EntityType,
    Guid? EntityId,
    string? ClientKey,
    string FieldKey);
