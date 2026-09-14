using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Common.Models;

public record PagedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
)
{
    /// <summary>
    /// Optional operational queue synchronization metadata. It is absent on every legacy paged
    /// endpoint and is therefore additive for existing consumers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageSyncMetadata? Sync { get; init; }
}
