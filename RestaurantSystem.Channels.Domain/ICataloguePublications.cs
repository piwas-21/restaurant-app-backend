using System.Text.Json;

namespace RestaurantSystem.Channels.Domain;

public sealed record CataloguePublication(Guid Id, string MappingHash, string SourceRevision, string Revision,
    JsonElement Menu, JsonElement PreviousMenu, string State, string? ProviderHash, DateTimeOffset? VerifiedAt,
    JsonElement? MappingSnapshot = null);

public interface ICataloguePublications
{
    Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken);
    async Task<CataloguePublication?> Find(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
    {
        var latest = await Latest(binding, cancellationToken);
        return latest?.Id == id ? latest : null;
    }
    Task<CataloguePublication> Begin(AvailabilityBinding binding, string mappingHash, string sourceRevision,
        string revision, JsonElement menu, JsonElement previousMenu, CancellationToken cancellationToken,
        JsonElement? mappingSnapshot = null);
    Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken);
}
