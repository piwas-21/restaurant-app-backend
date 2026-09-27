namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed record CatalogueCurrentRevisionMetadata(
    string TemplateId,
    int? Revision,
    string? ContentHash,
    bool Withdrawn,
    bool? AdoptedRevisionWithdrawn);
