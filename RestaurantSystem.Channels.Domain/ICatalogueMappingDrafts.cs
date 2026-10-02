using System.Text.Json;

namespace RestaurantSystem.Channels.Domain;

public sealed record CatalogueMappingDraft(string Revision, string MappingRevision, JsonElement Snapshot,
    Guid ActorId, DateTimeOffset UpdatedAt);

public interface ICatalogueMappingDrafts
{
    Task<CatalogueMappingDraft?> Read(AvailabilityBinding binding, CancellationToken cancellationToken);
    Task<bool> Save(AvailabilityBinding binding, CatalogueMappingDraft draft, string? expectedRevision,
        CancellationToken cancellationToken);
}
