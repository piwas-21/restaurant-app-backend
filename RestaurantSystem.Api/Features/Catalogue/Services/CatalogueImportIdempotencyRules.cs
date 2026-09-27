using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueImportIdempotencyRules
{
    public static bool CanReplayResult(CatalogueImportSession session, string key, int expectedVersion) =>
        session.LastImportIdempotencyKey == key && session.LastImportExpectedVersion == expectedVersion &&
        !string.IsNullOrWhiteSpace(session.LastImportResultJson) &&
        session.Status is CatalogueImportStatus.PartiallyImported or CatalogueImportStatus.Failed;

    public static bool CanStartAttempt(CatalogueImportSession session, string key, int expectedVersion)
    {
        if (expectedVersion < 1)
        {
            return false;
        }

        return session.Status switch
        {
            CatalogueImportStatus.Draft => session.Version == expectedVersion &&
                (session.LastImportIdempotencyKey is null || session.LastImportIdempotencyKey == key),
            CatalogueImportStatus.PartiallyImported or CatalogueImportStatus.Failed =>
                session.Version == expectedVersion,
            CatalogueImportStatus.Importing => session.LastImportIdempotencyKey == key &&
                session.LastImportExpectedVersion == expectedVersion,
            _ => false
        };
    }
}
