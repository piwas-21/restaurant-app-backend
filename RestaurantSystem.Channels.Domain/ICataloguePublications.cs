using System.Text.Json;

namespace RestaurantSystem.Channels.Domain;

public sealed record CataloguePublication(Guid Id, string MappingHash, string SourceRevision, string Revision,
    JsonElement Menu, JsonElement PreviousMenu, string State, string? ProviderHash, DateTimeOffset? VerifiedAt);

public interface ICataloguePublications
{
    Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<CataloguePublication> Begin(AvailabilityBinding binding, string mappingHash, string sourceRevision,
        string revision, JsonElement menu, JsonElement previousMenu, CancellationToken cancellationToken);
    Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken);
}
